using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CaptureOk.Core.Geometry;
using CaptureOk.Core.Imaging;
using CaptureOk.Native;

namespace CaptureOk.Capture;

/// <summary>
/// 스크롤 캡처: 고른 영역 가운데에서 마우스 휠을 굴려 내리며 화면이 멈출 때마다 찍고, 겹치는 곳을 찾아 이어 붙인다.
/// 끝에 닿으면(더 안 움직임)·최대 길이에 닿으면·Esc나 마우스 단추를 누르면 멈춘다.
/// 캡처하는 동안 사용자의 마우스·Esc 입력은 대상 프로그램에 보내지 않는다 (멈춤 클릭이 링크를 누르지 않게).
/// 진행 표시 창은 캡처에 찍히지 않게 한다.
/// </summary>
internal static class ScrollCapture
{
    public const int MaxHeight = 30000;
    private const int MinRegionHeight = 60;
    private static readonly TimeSpan SettlePoll = TimeSpan.FromMilliseconds(70);
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromMilliseconds(900);

    public sealed record Result(BitmapSource Image, string? Notice);

    public static async Task<Result> RunAsync(PixelRect rect)
    {
        NativeMethods.GetCursorPos(out var originalCursor);
        var monitor = ScreenGrabber.CurrentLayout().MonitorAt(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        var indicator = new Indicator(rect, monitor);
        StopWatcher? stop = null;
        try
        {
            indicator.Show();
            await WaitButtonsReleased(); // 영역을 고른 클릭을 "멈춤"으로 보지 않게
            stop = new StopWatcher();
            FocusTarget(rect); // "비활성 창 스크롤"이 꺼져 있어도 휠이 대상 창으로 가게
            var first = await SettledFrame(rect, previous: null, stop);
            if (rect.Height < MinRegionHeight)
                return new Result(ToImage(first, rect.Width, rect.Height), $"영역이 너무 낮아(세로 {MinRegionHeight}px 미만) 한 화면만 찍었습니다");

            // 오른쪽 스크롤 막대(손잡이가 매번 움직임)는 비교에서 뺀다 — 화면 배율만큼 넓게
            var stitcher = new ScrollStitcher(rect.Width, rect.Height, MaxHeight, ignoreRightColumns: (int)Math.Ceiling(24 * monitor.Scale));
            stitcher.Add(first);
            _traceStep = 0;
            Trace(first, rect, $"첫 화면 {rect}");
            var notice = await ScrollLoop(rect, monitor.Bounds, stitcher, first, stop, indicator);
            return new Result(Build(stitcher, first, rect, ref notice), notice);
        }
        finally
        {
            if (stop is not null) await stop.CloseAsync();
            indicator.Close();
            NativeMethods.SetCursorPos(originalCursor.X, originalCursor.Y);
        }
    }

    private static async Task<string?> ScrollLoop(PixelRect rect, PixelRect monitor, ScrollStitcher stitcher, byte[] first, StopWatcher stop, Indicator indicator)
    {
        var previous = first;
        var delta = ScrollPacing.Notch;
        var minDelta = ScrollPacing.MinDelta;
        var maxDelta = ScrollPacing.MaxDelta; // 너무 많이 움직여 놓친 적이 있으면 그보다 작게만 (되돌렸다 다시 크게 굴리기를 되풀이하지 않게)
        double pxPerDelta = 0;
        var backoffs = 0;
        var raised = false;
        try
        {
            while (true)
            {
                if (stop.Requested) return "멈췄습니다";
                await Wheel(rect, monitor, -delta);
                var frame = await SettledFrame(rect, previous, stop);
                if (stop.Requested) return "멈췄습니다";
                var expected = pxPerDelta > 0 ? (int)Math.Round(pxPerDelta * delta) : 0;
                var step = await Task.Run(() => stitcher.Add(frame, expected));
                Trace(frame, rect, $"휠 {delta} 예상 {expected} → {step} 이동 {stitcher.LastShift} 막대 {stitcher.Bars.Top}/{stitcher.Bars.Bottom} 높이 {stitcher.Height}");
                switch (step)
                {
                    case StitchStep.Appended:
                        previous = frame;
                        backoffs = 0;
                        if (raised) minDelta = ScrollPacing.Notch; // 한 칸보다 작게 굴리면 안 움직이는 프로그램
                        raised = false;
                        pxPerDelta = (double)stitcher.LastShift / delta;
                        delta = Math.Clamp(ScrollPacing.NextDelta(stitcher.LastShift, delta, stitcher.BandHeight), minDelta, Math.Max(minDelta, maxDelta));
                        indicator.Update(stitcher.Height);
                        continue;
                    case StitchStep.Ended:
                        if (delta < ScrollPacing.Notch && !raised)
                        {
                            raised = true;
                            delta = ScrollPacing.Notch;
                            continue;
                        }
                        return stitcher.Scrolled ? null : "화면이 스크롤되지 않았습니다. 스크롤되는 곳을 골라 다시 해 보세요";
                    case StitchStep.NoOverlap when backoffs < 3 && delta > minDelta:
                        // 너무 많이 움직여 겹치는 곳이 없어졌을 수 있다: 되돌리고 조금만 굴려 본다
                        await Wheel(rect, monitor, delta);
                        await SettledFrame(rect, previous: null, stop);
                        delta = Math.Max(minDelta, ScrollPacing.Backoff(delta));
                        maxDelta = delta;
                        backoffs++;
                        continue;
                    case StitchStep.NoOverlap:
                        return "화면이 크게 바뀌어 이어 붙이지 못한 곳에서 멈췄습니다";
                    default:
                        return $"최대 길이({MaxHeight:N0}px)에 닿아 멈췄습니다";
                }
            }
        }
        catch (InvalidOperationException ex)
        {
            // 잠금 화면 등으로 화면을 찍지 못함 — 그때까지 붙인 것을 연다
            AppLog.Write($"스크롤 캡처 중 화면 찍기 실패: {ex.Message}");
            return "화면을 찍지 못해 멈췄습니다";
        }
    }

    /// <summary>
    /// 단계마다 판정을 로그에 남긴다. 환경 변수 CAPTUREOK_SCROLL_DUMP에 폴더를 주면 그 단계의 화면도 PNG로 남긴다
    /// (이어 붙이기가 안 되는 사이트를 테스트로 다시 돌려 보려고).
    /// </summary>
    private static void Trace(byte[] frame, PixelRect rect, string message)
    {
        _traceStep++;
        AppLog.Write($"스크롤 {_traceStep}: {message}");
        var folder = Environment.GetEnvironmentVariable("CAPTUREOK_SCROLL_DUMP");
        if (string.IsNullOrEmpty(folder)) return;
        try
        {
            Directory.CreateDirectory(folder);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(ToImage(frame, rect.Width, rect.Height)));
            using var file = File.Create(Path.Combine(folder, $"frame-{_traceStep:D3}.png"));
            encoder.Save(file);
            File.AppendAllText(Path.Combine(folder, "steps.txt"), $"{_traceStep} {message}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"스크롤 기록 실패: {ex.Message}");
        }
    }

    private static int _traceStep;

    /// <summary>이어 붙인 결과를 그림으로. 너무 커서 메모리가 모자라면 첫 화면만.</summary>
    private static BitmapSource Build(ScrollStitcher stitcher, byte[] first, PixelRect rect, ref string? notice)
    {
        try
        {
            var height = stitcher.Height;
            var bitmap = new WriteableBitmap(rect.Width, height, 96, 96, PixelFormats.Bgra32, null);
            bitmap.Lock();
            try
            {
                unsafe
                {
                    stitcher.CopyTo(new Span<byte>((void*)bitmap.BackBuffer, bitmap.BackBufferStride * height), bitmap.BackBufferStride);
                }
                bitmap.AddDirtyRect(new Int32Rect(0, 0, rect.Width, height));
            }
            finally
            {
                bitmap.Unlock();
            }
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is OutOfMemoryException or ArgumentException or COMException)
        {
            AppLog.Write($"스크롤 캡처 결과 만들기 실패: {ex.GetType().Name}: {ex.Message}");
            notice = "메모리가 부족해 첫 화면만 열었습니다";
            return ToImage(first, rect.Width, rect.Height);
        }
    }

    internal static void FocusTarget(PixelRect rect)
    {
        var center = new NativeMethods.POINT { X = rect.X + rect.Width / 2, Y = rect.Y + rect.Height / 2 };
        var window = NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(center), NativeMethods.GA_ROOT);
        if (window == 0) return;
        NativeMethods.GetWindowThreadProcessId(window, out var pid);
        if (pid != Environment.ProcessId) NativeMethods.SetForegroundWindow(window);
    }

    private static async Task WaitButtonsReleased()
    {
        for (var i = 0; i < 40 && ((NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) != 0
            || (NativeMethods.GetAsyncKeyState(NativeMethods.VK_RBUTTON) & 0x8000) != 0); i++)
            await Task.Delay(25);
    }

    /// <summary>
    /// 영역 가운데에서 휠을 굴리고(음수 = 아래로), 잠시 뒤 커서를 영역 밖으로 옮긴다 —
    /// 커서 아래 줄을 강조하는 프로그램(탐색기 목록 등)의 강조가 화면마다 달라 이어 붙이기를 방해하지 않게.
    /// </summary>
    private static async Task Wheel(PixelRect rect, PixelRect monitor, int delta)
    {
        NativeMethods.SetCursorPos(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_WHEEL, 0, 0, delta, 0);
        await Task.Delay(40); // 휠이 그 자리의 창으로 간 뒤에 옮긴다
        var (x, y) = ScrollPacing.ParkPoint(rect, monitor);
        NativeMethods.SetCursorPos(x, y);
    }

    /// <summary>부드러운 스크롤이 끝날 때까지(연속 두 장이 같을 때까지, 최대 0.9초) 기다린 화면. 멈춤을 누르면 바로.</summary>
    private static async Task<byte[]> SettledFrame(PixelRect rect, byte[]? previous, StopWatcher stop)
    {
        var started = DateTime.UtcNow;
        await Task.Delay(SettlePoll);
        var last = ScreenGrabber.GrabRegionPixels(rect);
        while (DateTime.UtcNow - started < SettleTimeout && !stop.Requested)
        {
            await Task.Delay(SettlePoll);
            var next = ScreenGrabber.GrabRegionPixels(rect);
            var settled = next.AsSpan().SequenceEqual(last);
            last = next;
            // 아직 안 움직였으면(앞 화면과 같음) 조금 더 기다려 본다 — 늦게 움직이는 프로그램
            if (settled && (previous is null || !next.AsSpan().SequenceEqual(previous) || DateTime.UtcNow - started > TimeSpan.FromMilliseconds(450)))
                break;
        }
        return last;
    }

    private static BitmapSource ToImage(byte[] pixels, int width, int height)
    {
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        image.Freeze();
        return image;
    }

    /// <summary>
    /// 캡처하는 동안 사용자의 마우스(움직임·휠·단추)와 Esc를 가로챈다: 단추나 Esc는 멈춤 요청으로 받고, 대상 프로그램에는 보내지 않는다.
    /// 우리가 보낸 휠(프로그램이 넣은 입력)은 그대로 보낸다. 훅을 걸지 못하면 키 상태를 읽어 멈춤만 알아챈다.
    /// </summary>
    private sealed class StopWatcher
    {
        private readonly NativeMethods.LowLevelHookProc _mouseProc;
        private readonly NativeMethods.LowLevelHookProc _keyProc;
        private nint _mouseHook, _keyHook;
        private volatile bool _requested;
        private volatile int _buttonsDown;

        public StopWatcher()
        {
            _mouseProc = OnMouse; // 대리자가 수거되지 않게 필드로 붙잡아 둔다
            _keyProc = OnKey;
            var module = NativeMethods.GetModuleHandle(null);
            _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseProc, module, 0);
            _keyHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyProc, module, 0);
            if (_mouseHook == 0 || _keyHook == 0) AppLog.Write($"스크롤 캡처 입력 훅 실패: {Marshal.GetLastWin32Error()}");
        }

        public bool Requested => _requested || ((_mouseHook == 0 || _keyHook == 0) && PolledStop());

        private static bool PolledStop() =>
            (NativeMethods.GetAsyncKeyState(NativeMethods.VK_ESCAPE) & 0x8000) != 0
            || (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) != 0
            || (NativeMethods.GetAsyncKeyState(NativeMethods.VK_RBUTTON) & 0x8000) != 0;

        private nint OnMouse(int code, nint wParam, nint lParam)
        {
            if (code >= 0)
            {
                var info = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                if ((info.flags & NativeMethods.LLMHF_INJECTED) == 0)
                {
                    switch ((int)wParam)
                    {
                        case NativeMethods.WM_LBUTTONDOWN or NativeMethods.WM_RBUTTONDOWN or NativeMethods.WM_MBUTTONDOWN or NativeMethods.WM_XBUTTONDOWN:
                            _buttonsDown++;
                            _requested = true;
                            break;
                        case NativeMethods.WM_LBUTTONDOWN + 1 or NativeMethods.WM_RBUTTONDOWN + 1 or NativeMethods.WM_MBUTTONDOWN + 1 or NativeMethods.WM_XBUTTONDOWN + 1:
                            if (_buttonsDown > 0) _buttonsDown--;
                            break;
                    }
                    return 1; // 커서도 우리가 둔 자리에 그대로 (사용자 움직임과 다투지 않게)
                }
            }
            return NativeMethods.CallNextHookEx(_mouseHook, code, wParam, lParam);
        }

        private nint OnKey(int code, nint wParam, nint lParam)
        {
            if (code >= 0)
            {
                var info = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                if (info.vkCode == NativeMethods.VK_ESCAPE && (info.flags & NativeMethods.LLKHF_INJECTED) == 0)
                {
                    if ((int)wParam is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN) _requested = true;
                    return 1;
                }
            }
            return NativeMethods.CallNextHookEx(_keyHook, code, wParam, lParam);
        }

        /// <summary>멈춤 클릭을 뗄 때까지(최대 1.5초) 기다렸다가 훅을 푼다 — 뗀 입력만 대상 프로그램에 가지 않게.</summary>
        public async Task CloseAsync()
        {
            for (var i = 0; i < 30 && _buttonsDown > 0; i++) await Task.Delay(50);
            if (_mouseHook != 0) NativeMethods.UnhookWindowsHookEx(_mouseHook);
            if (_keyHook != 0) NativeMethods.UnhookWindowsHookEx(_keyHook);
            _mouseHook = _keyHook = 0;
        }
    }

    /// <summary>"스크롤 캡처 중" 작은 창. 영역과 겹치지 않는 곳에 두고, 캡처에 찍히지 않게 한다.</summary>
    private sealed class Indicator : Window
    {
        private readonly TextBlock _text = new()
        {
            Foreground = Brushes.White, FontSize = 13, TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0),
        };

        public Indicator(PixelRect region, MonitorInfo monitor)
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Width = 460;
            Height = 34;
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x20, 0x20, 0x20));
            Content = _text;
            Update(0);
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = monitor.Bounds.X / monitor.Scale; // 대략 그 모니터에 띄운 뒤, 물리 픽셀로 정확히 옮긴다
            Top = monitor.Bounds.Y / monitor.Scale;
            SourceInitialized += (_, _) =>
            {
                var handle = new WindowInteropHelper(this).Handle;
                var ex = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE);
                NativeMethods.SetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE, (nint)(ex | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW));
                var hidden = NativeMethods.SetWindowDisplayAffinity(handle, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
                NativeMethods.GetWindowRect(handle, out var r);
                var place = ScrollPacing.IndicatorPlace(region, monitor.Bounds, r.Right - r.Left, r.Bottom - r.Top, monitor.Scale, hidden);
                NativeMethods.SetWindowPos(handle, NativeMethods.HWND_TOPMOST, place.X, place.Y, 0, 0, NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
                if (!place.Visible) Opacity = 0; // 캡처에서 숨길 수 없고 놓을 곳도 없으면 보이지 않게
            };
        }

        public void Update(int height) =>
            _text.Text = height == 0 ? "스크롤 캡처 중 — Esc 또는 클릭으로 멈춤" : $"스크롤 캡처 중 {height:N0}px — Esc 또는 클릭으로 멈춤";
    }
}
