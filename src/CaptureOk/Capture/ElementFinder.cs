using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Threading;
using CaptureOk.Core.Geometry;
using CaptureOk.Core.Windows;

namespace CaptureOk.Capture;

/// <summary>
/// 커서 아래 창 안에서 커서를 포함하는 화면 요소 경계 사슬(창 → … → 가장 안쪽)과 각 요소가 세로로 스크롤되는지를 UI Automation으로 구한다.
/// 다른 프로그램을 묻는 일이라 느릴 수 있어 전용 스레드에서 하고, 가장 최근 요청만 처리한다(밀린 요청은 버림).
/// 결과는 UI 스레드로 돌려준다. 실패하면 그때까지 구한 사슬(최소한 창 경계)을 돌려준다.
/// </summary>
internal sealed class ElementFinder : IDisposable
{
    private const int MaxDepth = 40;
    /// <summary>자식이 이보다 많은 요소(행이 수천 개인 목록 등)는 더 내려가지 않는다 — 한 번 묻는 데 몇 초씩 걸리지 않게.</summary>
    private const int MaxChildren = 2000;

    private readonly Dispatcher _ui;
    private readonly Action<Request, IReadOnlyList<ElementLevel>> _done;
    private readonly Thread _thread;
    private readonly AutoResetEvent _signal = new(false);
    private readonly Lock _gate = new();
    private Request? _pending;
    private int _latest;
    private volatile bool _stopped;

    public readonly record struct Request(nint Window, PixelRect WindowBounds, int X, int Y, int Id);

    public ElementFinder(Dispatcher ui, Action<Request, IReadOnlyList<ElementLevel>> done)
    {
        _ui = ui;
        _done = done;
        // UI Automation 클라이언트는 MTA 스레드에서 쓰는 것이 권장된다 (새 Thread의 기본값).
        _thread = new Thread(Run) { IsBackground = true, Name = "CaptureOK 요소 감지" };
        _thread.Start();
    }

    /// <summary>이 위치의 요소 사슬을 구해 달라고 한다. 앞서 기다리던 요청은 버린다.</summary>
    public void Find(nint window, PixelRect windowBounds, int x, int y)
    {
        lock (_gate)
        {
            _pending = new Request(window, windowBounds, x, y, ++_latest);
        }
        _signal.Set();
    }

    private bool IsStale(Request request) => _stopped || Volatile.Read(ref _latest) != request.Id;

    private void Run()
    {
        while (!_stopped)
        {
            _signal.WaitOne();
            Request? request;
            lock (_gate)
            {
                request = _pending;
                _pending = null;
            }
            if (request is not { } r || _stopped) continue;
            try
            {
                var chain = Chain(r);
                if (IsStale(r)) continue;
                _ui.BeginInvoke(() => { if (!_stopped) _done(r, chain); });
            }
            catch (Exception ex)
            {
                // 이 스레드에서 처리되지 않은 예외는 앱 전체를 끄므로 무엇이든 여기서 막는다 (예: 관리자 권한 창)
                AppLog.Write($"요소 감지 실패: {ex.GetType().Name}: {ex.Message}");
            }
        }
        _signal.Dispose();
    }

    private List<ElementLevel> Chain(Request r)
    {
        var chain = new List<ElementLevel> { new(r.WindowBounds, false) };
        try
        {
            // 자식의 경계를 한 번에 받아 오고(단계마다 다른 프로그램을 한 번만 부름), 고른 자식으로 다음 단계를 다시 묻는다.
            // 다음 단계를 물으려면 실제 요소 참조가 필요해서 Full.
            var cache = new CacheRequest { TreeScope = TreeScope.Element | TreeScope.Children, AutomationElementMode = AutomationElementMode.Full };
            cache.Add(AutomationElement.BoundingRectangleProperty);
            cache.Add(AutomationElement.IsOffscreenProperty);
            cache.Add(ScrollPattern.VerticallyScrollableProperty); // 스크롤 캡처가 고를 영역
            AutomationElement element;
            using (cache.Activate()) element = AutomationElement.FromHandle(r.Window);
            chain[0] = chain[0] with { Scrollable = Scrolls(element) };
            for (var depth = 0; depth < MaxDepth && !IsStale(r); depth++)
            {
                var candidates = new List<(AutomationElement Element, PixelRect Rect)>();
                var children = element.CachedChildren;
                if (children.Count > MaxChildren) break;
                foreach (AutomationElement child in children)
                {
                    if (child.Cached.IsOffscreen) continue;
                    var b = child.Cached.BoundingRectangle;
                    if (b.IsEmpty || b.Width < 1 || b.Height < 1) continue;
                    var rect = new PixelRect((int)Math.Round(b.X), (int)Math.Round(b.Y), (int)Math.Round(b.Width), (int)Math.Round(b.Height)).Intersect(r.WindowBounds);
                    // 너무 작은 요소(분할선·초점 표시 등)는 고르지 않는다 — 그 안으로 내려가면 진짜 내용을 놓친다
                    if (rect.Width < ElementChain.MinSide || rect.Height < ElementChain.MinSide || !rect.Contains(r.X, r.Y)) continue;
                    candidates.Add((child, rect));
                }
                if (candidates.Count == 0) break;
                // 겹치는 형제 중에서는 더 작은(안쪽) 것을 고른다. 경계가 똑같은 형제가 여럿이면 안에 내용이 있는 것 —
                // 크롬 계열 창은 내용과 같은 크기의 빈 그리기용 창("Intermediate D3D Window")을 위에 두므로.
                var smallest = candidates.Min(c => (long)c.Rect.Width * c.Rect.Height);
                var tied = candidates.Where(c => (long)c.Rect.Width * c.Rect.Height == smallest).ToList();
                AutomationElement? next = null;
                var nextRect = tied[0].Rect;
                foreach (var (candidate, rect) in tied)
                {
                    AutomationElement updated;
                    using (cache.Activate()) updated = candidate.GetUpdatedCache(cache);
                    next ??= updated;
                    if (tied.Count == 1 || updated.CachedChildren.Count > 0)
                    {
                        next = updated;
                        nextRect = rect;
                        break;
                    }
                }
                chain.Add(new ElementLevel(nextRect, Scrolls(next!)));
                element = next!;
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException or ArgumentException
            or TimeoutException or UnauthorizedAccessException or NotImplementedException or NotSupportedException)
        {
            // 응답하지 않거나 사라진 창, 권한이 다른 창: 그때까지 구한 사슬로
        }
        return chain;
    }

    private static bool Scrolls(AutomationElement element) =>
        element.GetCachedPropertyValue(ScrollPattern.VerticallyScrollableProperty, ignoreDefaultValue: true) is true;

    public void Dispose()
    {
        _stopped = true;
        _signal.Set();
    }
}
