using System.Windows.Input;
using System.Windows.Media.Imaging;
using CaptureOk.Core.Geometry;
using CaptureOk.Core.Selection;
using CaptureOk.Core.Settings;
using CaptureOk.Core.Windows;
using CaptureOk.Native;
using CaptureMode = CaptureOk.Core.Settings.CaptureMode;

namespace CaptureOk.Capture;

/// <param name="RecordGif">영역 지정 중 우클릭: 이 영역을 GIF로 녹화한다</param>
internal sealed record CaptureResult(BitmapSource Image, PixelRect Rect, CaptureMode Mode, bool RecordGif = false);

/// <summary>캡처 한 번의 흐름: 정지 캡처 → (필요하면) 오버레이에서 범위 선택 → 잘라낸 결과.</summary>
internal sealed class CaptureSession
{
    private readonly CaptureMode _mode;
    private CaptureMode _resultMode; // 영역 지정 중 가운데 클릭이면 스크롤 캡처로 끝난다
    private bool _recordGif; // 영역 지정 중 우클릭이면 GIF 녹화로 끝난다
    private bool _gifWaitsForMove; // 우클릭으로 선택을 푼 직전: 연달아 누른 우클릭이 메뉴를 열지 않게 커서가 움직이기를 기다린다
    private PixelRect? _menuRect; // 우클릭 메뉴가 열려 있는 동안 강조를 고정할 칸 (메뉴에서 고르면 이 칸을 찍는다)
    private readonly CaptureSettings _settings;
    private readonly IReadOnlyList<WindowInfo> _windows;
    private readonly PrecisionMover _mover = new();
    private readonly List<OverlayWindow> _overlays = [];
    private readonly TaskCompletionSource<CaptureResult?> _done = new();
    private RegionSelection _selection;
    private WindowInfo? _hoveredWindow;
    private bool _buttonDown;
    private bool _finished;

    private readonly CursorShot? _cursor;
    private ElementFinder? _finder;
    private ElementChain? _chain; // 강조할 요소 사슬 (커서 아래 창 안)

    private CaptureSession(CaptureMode mode, CaptureSettings settings, BitmapSource image, ScreenLayout layout, IReadOnlyList<WindowInfo> windows,
        CursorShot? cursor)
    {
        _mode = mode;
        _resultMode = mode;
        _cursor = cursor;
        _settings = settings;
        Image = image;
        Layout = layout;
        _windows = windows;
        _selection = NewSelection(0);
        if (Detects) _finder = new ElementFinder(System.Windows.Threading.Dispatcher.CurrentDispatcher, OnDetected);
    }

    public BitmapSource Image { get; }
    public ScreenLayout Layout { get; }
    public (int X, int Y) CursorPosition => _selection.Cursor;
    /// <summary>
    /// 요소·스크롤 캡처, 그리고 영역 지정(설정이 켜져 있을 때)에서 마우스 아래 창·요소를 자동으로 강조한다.
    /// 스크롤 캡처는 실제로 스크롤되는 영역을 클릭으로 고른다 (드래그로 영역을 지정하지 않음).
    /// </summary>
    private bool Detects => _mode is CaptureMode.Element or CaptureMode.Scroll || (_mode == CaptureMode.Region && _settings.AutoDetectElements);

    public bool IsHoverHighlight => (_mode == CaptureMode.Window || Detects) && _selection.Phase == SelectionPhase.Hovering;

    /// <summary>강조한 칸 옆에 크기를 보일지 (창 캡처는 창 전체라 보이지 않음).</summary>
    public bool ShowHighlightSize => _mode != CaptureMode.Window || _selection.Phase != SelectionPhase.Hovering;
    public bool ShowGuides => _settings.ShowGuides && _menuRect is null && _mode is CaptureMode.Region or CaptureMode.Element or CaptureMode.Scroll && _selection.Phase == SelectionPhase.Hovering;
    public bool ShowMagnifier => _settings.ShowMagnifier && _menuRect is null; // 메뉴를 가리지 않게

    public PixelRect? HighlightRect => _menuRect ?? (IsHoverHighlight ? HoveredRect : _selection.Rect);

    /// <summary>화면에 보여 줄 조작 안내 (고르는 중에만).</summary>
    public string? Hint => !_settings.ShowCaptureHint || _menuRect is not null ? null : _selection.Phase switch
    {
        SelectionPhase.Hovering => CaptureHint.For(_mode, Detects),
        SelectionPhase.Dragging when _mode == CaptureMode.Region => CaptureHint.ForDragging(_settings.ConfirmOnRelease),
        _ => null,
    };

    /// <summary>지금 강조하는 칸: 감지한 요소, 아직 없으면 창 전체.</summary>
    // 감지 결과가 오기 전에 커서가 움직였으면, 이미 벗어난 안쪽 요소 대신 커서를 포함하는 바깥 단계로.
    private PixelRect? HoveredRect =>
        (Detects ? _chain?.CurrentContaining(_selection.Cursor.X, _selection.Cursor.Y) : null) is { } element ? Layout.Clamp(element)
        : _hoveredWindow is { } w ? Layout.Clamp(w.Bounds) : null;

    private const int MaxDetectRetries = 3;
    private static readonly TimeSpan ScrollPickTimeout = TimeSpan.FromMilliseconds(2500); // 답이 아예 없을 때만 쓴다 (다시 묻는 시간은 0.4초 × 3)
    private int _detectRetries; // 요소를 다시 물은 횟수 (창이 바뀌면 0)
    private bool _middlePressed;
    private bool _awaitingScrollTarget; // 가운데 클릭 뒤 스크롤 영역을 찾는 중
    private (int X, int Y) _scrollPickPoint; // 가운데 클릭을 뗀 자리
    private PixelRect? _pressedElement; // 누른 순간 강조하던 칸 (그때 아무것도 고르지 않은 상태였을 때만)
    private int _wheelDelta;

    public static Task<CaptureResult?> RunAsync(CaptureMode mode, CaptureSettings settings)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        // 창 목록은 정지 캡처 직전에 찍어야 화면과 맞는다.
        var needsWindows = mode is CaptureMode.Window or CaptureMode.Element or CaptureMode.Scroll or CaptureMode.Region; // 영역 지정: 자동 감지를 꺼도 가운데 클릭에 필요
        var windows = needsWindows ? WindowEnumerator.Snapshot(settings.ExcludedWindows) : [];
        var (image, layout) = ScreenGrabber.GrabVirtualScreen();
        var cursorShot = settings.IncludeCursor ? ScreenGrabber.GrabCursor() : null; // 결과에만 넣는다
        NativeMethods.GetCursorPos(out var cursor);
        AppLog.Write($"{mode} 정지 캡처 {clock.ElapsedMilliseconds}ms (창 {windows.Count}개)");

        switch (mode)
        {
            case CaptureMode.FullScreen:
                return Task.FromResult<CaptureResult?>(Cut(image, layout, layout.MonitorAt(cursor.X, cursor.Y).Bounds, mode, cursorShot));
            case CaptureMode.LastRegion:
                var last = settings.LastRegion is { } r ? layout.Clamp(r) : default;
                if (last.IsEmpty) last = CenteredOnPrimary(layout, CaptureSettings.DefaultLastRegion);
                return Task.FromResult<CaptureResult?>(Cut(image, layout, last, mode, cursorShot));
        }

        var session = new CaptureSession(mode, settings, image, layout, windows, cursorShot);
        session.Start(cursor.X, cursor.Y);
        AppLog.Write($"{mode} 오버레이 표시까지 {clock.ElapsedMilliseconds}ms");
        return session._done.Task;
    }

    private static CaptureResult Cut(BitmapSource image, ScreenLayout layout, PixelRect rect, CaptureMode mode, CursorShot? cursor)
    {
        var cut = ScreenGrabber.Crop(image, layout, rect);
        var result = new CaptureResult(cursor is { } c ? ScreenGrabber.WithCursor(cut, rect, c) : cut, rect, mode);
        cursor?.Dispose();
        return result;
    }

    private static PixelRect CenteredOnPrimary(ScreenLayout layout, PixelSize size)
    {
        var p = layout.Primary.Bounds;
        return layout.Clamp(new PixelRect(p.X + (p.Width - size.Width) / 2, p.Y + (p.Height - size.Height) / 2, size.Width, size.Height));
    }

    private RegionSelection NewSelection(int fixedIndex) =>
        _mode == CaptureMode.FixedSize
            ? new RegionSelection(Layout, _settings.FixedSizes[fixedIndex])
            : new RegionSelection(Layout, confirmOnRelease: _settings.ConfirmOnRelease);

    private void Start(int x, int y)
    {
        _selection.MouseMove(x, y, shift: false);
        UpdateHover(x, y);
        _mover.Reset(x, y);

        foreach (var monitor in Layout.Monitors)
        {
            var overlay = new OverlayWindow(this, monitor);
            _overlays.Add(overlay);
            overlay.Show();
        }
        (_overlays.FirstOrDefault(o => o.Covers(x, y)) ?? _overlays[0]).ActivateForInput();
    }

    // ---- 입력 (OverlayWindow에서 호출) ----

    public void OnMouseMove()
    {
        if (_menuRect is not null) return;
        NativeMethods.GetCursorPos(out var raw);
        var slow = IsCtrlDown() && _selection.Phase == SelectionPhase.Dragging;
        var (x, y) = _mover.Move(raw.X, raw.Y, slow);
        if (slow && (x != raw.X || y != raw.Y)) NativeMethods.SetCursorPos(x, y);
        if ((x, y) != _selection.Cursor) _gifWaitsForMove = false;

        _selection.MouseMove(x, y, IsShiftDown());
        if (!_awaitingScrollTarget) UpdateHover(x, y); // 가운데 클릭 뒤에는 누른 자리의 창을 찍는다
        AfterInput();
    }

    /// <summary>커서 아래 창을 다시 찾고, 요소 감지를 맡긴다. 창이 바뀌면 그 창 전체부터 강조한다.</summary>
    private void UpdateHover(int x, int y)
    {
        if (_mode != CaptureMode.Window && !Detects) return;
        var window = WindowPicker.HitTest(_windows, x, y);
        if (window?.Handle != _hoveredWindow?.Handle)
        {
            _chain = null;
            _detectRetries = 0;
        }
        _hoveredWindow = window;
        if (Detects && window is not null && _selection.Phase == SelectionPhase.Hovering)
            _finder?.Find(window.Handle, window.Bounds, x, y);
    }

    /// <summary>감지 결과: 그 사이 커서가 다른 창으로 갔거나 결과의 가장 안쪽 칸 밖으로 나갔으면 버린다.</summary>
    private void OnDetected(ElementFinder.Request request, IReadOnlyList<ElementLevel> levels)
    {
        if (_finished || request.Window != _hoveredWindow?.Handle) return;
        var chain = new ElementChain(levels, preferScrollable: _mode == CaptureMode.Scroll);
        var (x, y) = _awaitingScrollTarget ? _scrollPickPoint : _selection.Cursor;
        if (chain.Innermost is not { } inner || !inner.Contains(x, y)) return;
        if (_chain?.Innermost != inner || _chain.HasScrollable != chain.HasScrollable) // 같은 요소면 휠로 고른 단계를 유지
        {
            if (_mode == CaptureMode.Scroll || _awaitingScrollTarget)
                AppLog.Write($"스크롤 영역 감지: {levels.Count}단계, 스크롤되는 곳 {string.Join(" ", levels.Where(l => l.Scrollable).Select(l => l.Rect.ToString()))}, 고른 곳 {chain.Current}");
            _chain = chain;
            foreach (var o in _overlays) o.Redraw();
        }
        if (_awaitingScrollTarget && (chain.HasScrollable || _detectRetries >= MaxDetectRetries)) FinishScrollPick();
        else RetryDetection(chain);
    }

    /// <summary>
    /// 창마다 조금 뒤 최대 3번 다시 묻는다 (스크롤 영역을 찾는 중이면 찾을 때까지만) —
    /// 웹 브라우저는 처음 물을 때 접근성 정보를 켜기 시작해, 그 뒤에야 페이지 안의 요소를 알려 준다.
    /// 그 사이에는 창 전체만 강조되므로, 커서를 움직이지 않아도 요소가 잡히게 한다.
    /// </summary>
    private void RetryDetection(ElementChain chain)
    {
        var wantsScrollable = _mode == CaptureMode.Scroll || _awaitingScrollTarget;
        if ((wantsScrollable && chain.HasScrollable) || _detectRetries >= MaxDetectRetries || _hoveredWindow is not { } window) return;
        _detectRetries++;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_finished || _menuRect is not null || _hoveredWindow?.Handle != window.Handle || _selection.Phase != SelectionPhase.Hovering) return;
            var (cx, cy) = _awaitingScrollTarget ? _scrollPickPoint : _selection.Cursor;
            _finder?.Find(window.Handle, window.Bounds, cx, cy);
        };
        timer.Start();
    }

    /// <summary>휠: 위 = 바깥 요소, 아래 = 안쪽 요소.</summary>
    public void OnWheel(int delta)
    {
        if (_middlePressed || _awaitingScrollTarget || _menuRect is not null) return; // 휠 단추를 누르다 살짝 굴러도 범위가 바뀌지 않게
        if (!Detects || _selection.Phase != SelectionPhase.Hovering || _chain is null) return;
        // 터치패드는 작은 값을 여러 번 보내므로 한 칸(120)이 모일 때마다 한 단계
        _wheelDelta += delta;
        var moved = false;
        while (Math.Abs(_wheelDelta) >= 120)
        {
            moved |= _wheelDelta > 0 ? _chain.Wider() : _chain.Narrower();
            _wheelDelta -= Math.Sign(_wheelDelta) * 120;
        }
        if (moved) AfterInput();
    }

    /// <summary>영역 지정·스크롤 캡처에서 가운데 버튼: 뗄 때 스크롤 영역을 골라 스크롤 캡처를 시작한다.</summary>
    public void OnMiddleDown() =>
        _middlePressed = _mode is CaptureMode.Region or CaptureMode.Scroll && !_buttonDown && !_awaitingScrollTarget && _menuRect is null && _selection.Phase == SelectionPhase.Hovering;

    public void OnMiddleUp()
    {
        if (!_middlePressed) return;
        _middlePressed = false;
        if (_finished || _buttonDown || _selection.Phase != SelectionPhase.Hovering) return;

        var (x, y) = _selection.Cursor;
        if (WindowPicker.HitTest(_windows, x, y) is not { } window) return;
        if (window.Handle != _hoveredWindow?.Handle)
        {
            _chain = null;
            _detectRetries = 0;
        }
        _hoveredWindow = window;
        _scrollPickPoint = (x, y);
        // 휠로 범위를 직접 골랐거나, 바로 이 자리의 스크롤 영역을 이미 알면 바로 시작
        if (_chain is { } chain && (chain.PickedWithWheel || (chain.HasScrollable && chain.Innermost is { } inner && inner.Contains(x, y))))
        {
            FinishScrollPick();
            return;
        }

        // 스크롤 영역을 아직 모른다 (웹 브라우저가 처음에 안 알려 줌, 자동 감지를 꺼 둠, 커서를 막 옮김):
        // 누른 자리를 물어보고, 답이 없어도 잠시 뒤 끝낸다. 그동안은 대기 커서를 보인다
        _awaitingScrollTarget = true;
        foreach (var o in _overlays) o.Cursor = Cursors.AppStarting;
        _finder ??= new ElementFinder(System.Windows.Threading.Dispatcher.CurrentDispatcher, OnDetected);
        _finder.Find(window.Handle, window.Bounds, x, y);
        var timeout = new System.Windows.Threading.DispatcherTimer { Interval = ScrollPickTimeout };
        timeout.Tick += (_, _) =>
        {
            timeout.Stop();
            if (_awaitingScrollTarget) FinishScrollPick();
        };
        timeout.Start();
    }

    /// <summary>찾은 스크롤 영역(없으면 창 전체)을 확정하고 스크롤 캡처로 끝낸다.</summary>
    private void FinishScrollPick()
    {
        _awaitingScrollTarget = false;
        foreach (var o in _overlays) o.Cursor = Cursors.Cross;
        if (_finished || _selection.Phase != SelectionPhase.Hovering) return;
        var (x, y) = _scrollPickPoint;
        if ((_chain?.ScrollTargetAt(x, y) ?? _hoveredWindow?.Bounds) is not { } target) return;
        _resultMode = CaptureMode.Scroll;
        _selection.Confirm(Layout.Clamp(target));
        AfterInput();
    }

    public void OnMouseDown()
    {
        if (_menuRect is not null) return;
        _buttonDown = true; // 뗄 때까지 오버레이를 유지 (뗀 신호가 아래 프로그램으로 가지 않게)
        if (_awaitingScrollTarget) return; // 곧 스크롤 캡처가 시작된다
        NativeMethods.GetCursorPos(out var p);
        _mover.Reset(p.X, p.Y);
        if (_mode == CaptureMode.Window)
        {
            if (_hoveredWindow is { } w) _selection.Confirm(w.Bounds);
        }
        else if (_mode is CaptureMode.Element or CaptureMode.Scroll)
        {
            if (HoveredRect is { } element) _selection.Confirm(element); // 요소·스크롤 캡처는 드래그 없이 칸만 고른다
        }
        else
        {
            // 아무것도 고르지 않은 상태에서 누른 경우에만 클릭 = 강조한 칸 (고른 영역 밖을 눌러 다시 시작할 때는 아님)
            _pressedElement = Detects && _selection.Phase == SelectionPhase.Hovering ? HoveredRect : null;
            _selection.MouseDown(p.X, p.Y);
        }
        AfterInput();
    }

    public void OnMouseUp()
    {
        if (_menuRect is not null) return;
        _buttonDown = false;
        var (x, y) = _selection.Cursor;
        var pressed = _pressedElement;
        _pressedElement = null;
        if (_selection.MouseUp(x, y, IsShiftDown()) && pressed is { } element) _selection.Confirm(element); // 클릭 = 누를 때 강조하던 칸
        AfterInput();
    }

    public void OnDoubleClick()
    {
        if (_menuRect is not null) return;
        _buttonDown = true; // 두 번째 클릭의 버튼 떼기까지 오버레이를 유지한다
        if (_awaitingScrollTarget) return;
        _selection.DoubleClick();
        AfterInput();
    }

    /// <summary>오버레이가 바깥 요인(Alt+F4 등)으로 닫히면 캡처를 취소한다.</summary>
    public void OnOverlayClosed()
    {
        if (_finished) return;
        _buttonDown = false;
        _selection.Key(SelectionKey.Escape, fast: false, alt: false);
        AfterInput();
    }

    public void OnRightClick()
    {
        if (_awaitingScrollTarget || _menuRect is not null) return; // 곧 스크롤 캡처가 시작된다 (Esc만 취소) / 메뉴가 받는다
        if (_mode is CaptureMode.Region or CaptureMode.Element && _selection.Phase == SelectionPhase.Hovering)
        {
            OpenMenu();
            return;
        }
        if (_mode == CaptureMode.Region && _selection.Phase == SelectionPhase.Dragging)
        {
            OpenMenuForDrag();
            return;
        }
        _buttonDown = false;
        var wasSelected = _selection.Phase == SelectionPhase.Selected;
        _selection.Key(SelectionKey.RightButton, fast: false, alt: false);
        _gifWaitsForMove = wasSelected && _selection.Phase == SelectionPhase.Hovering;
        AfterInput();
    }

    /// <summary>
    /// 아무것도 고르지 않은 채 우클릭: 강조한 칸(자동 감지를 껐으면 커서 아래 창)을 고정하고 메뉴를 띄운다.
    /// 메뉴가 열려 있는 동안 다른 입력은 받지 않는다.
    /// </summary>
    private void OpenMenu()
    {
        if (_middlePressed || _buttonDown || _gifWaitsForMove) return;
        var (x, y) = _selection.Cursor;
        if ((HoveredRect ?? WindowPicker.HitTest(_windows, x, y)?.Bounds) is not { } target) return;
        // 바탕 화면처럼 모니터를 넘는 칸은 커서가 있는 모니터만 찍는다
        var region = Layout.Clamp(target).Intersect(Layout.MonitorAt(x, y).Bounds);
        if (region.IsEmpty) return;
        ShowMenuFor(region);
    }

    /// <summary>왼쪽 버튼을 누른 채 우클릭: 끈 범위(거의 안 끌었으면 누를 때 강조하던 칸)로 메뉴를 띄운다. 버튼을 떼도 확정되지 않는다.</summary>
    private void OpenMenuForDrag()
    {
        var pressed = _pressedElement;
        _pressedElement = null;
        _buttonDown = false;
        if ((_selection.DetachDrag() ?? pressed) is { IsEmpty: false } region) ShowMenuFor(Layout.Clamp(region));
        else AfterInput();
    }

    private void ShowMenuFor(PixelRect region)
    {
        _menuRect = region;
        foreach (var o in _overlays) o.Redraw();
        var (x, y) = _selection.Cursor;
        (_overlays.FirstOrDefault(o => o.Covers(x, y)) ?? _overlays[0]).ShowMenu(RecordGifFromMenu, CloseMenu);
    }

    private void RecordGifFromMenu()
    {
        if (_finished || _menuRect is not { } region) return;
        _menuRect = null;
        _recordGif = true;
        _selection.Confirm(region);
        AfterInput();
    }

    /// <summary>메뉴를 고르지 않고 닫음: 영역 고르기로 돌아간다.</summary>
    private void CloseMenu()
    {
        if (_finished) return;
        _menuRect = null;
        OnMouseMove(); // 메뉴를 보는 사이 커서가 옮겨 갔을 수 있다
    }

    /// <returns>키를 처리했으면 true</returns>
    public bool OnKey(Key key)
    {
        if (_menuRect is not null) return false; // 메뉴가 받는다 (Esc = 메뉴만 닫음)
        if (_awaitingScrollTarget && key != Key.Escape) return true; // 곧 스크롤 캡처가 시작된다 (Esc만 취소)
        if (_mode == CaptureMode.FixedSize && key is >= Key.D1 and <= Key.D9 or >= Key.NumPad1 and <= Key.NumPad9)
        {
            var index = key >= Key.NumPad1 ? key - Key.NumPad1 : key - Key.D1;
            if (index < _settings.FixedSizes.Count)
            {
                var (cx, cy) = _selection.Cursor;
                _selection = NewSelection(index);
                _selection.MouseMove(cx, cy, shift: false);
                AfterInput();
            }
            return true;
        }

        // 아무것도 고르지 않은 채 Enter = 강조한 칸 (키보드만으로 요소 캡처)
        if (key == Key.Enter && Detects && _selection.Phase == SelectionPhase.Hovering && HoveredRect is { } hovered)
        {
            _buttonDown = false;
            _selection.Confirm(hovered);
            AfterInput();
            return true;
        }

        SelectionKey? mapped = key switch
        {
            Key.Left => SelectionKey.Left,
            Key.Up => SelectionKey.Up,
            Key.Right => SelectionKey.Right,
            Key.Down => SelectionKey.Down,
            Key.Enter => SelectionKey.Enter,
            Key.Escape => SelectionKey.Escape,
            _ => null,
        };
        if (mapped is null) return false;
        if (mapped is SelectionKey.Enter or SelectionKey.Escape) _buttonDown = false; // 키보드로 끝낼 때는 버튼 떼기를 기다리지 않는다

        if (_selection.Key(mapped.Value, IsShiftDown(), IsAltDown()))
        {
            var (x, y) = _selection.Cursor;
            NativeMethods.SetCursorPos(x, y);
            _mover.Reset(x, y);
            UpdateHover(x, y);
        }
        AfterInput();
        return true;
    }

    private void AfterInput()
    {
        if (_finished) return;
        // 확정·취소가 버튼을 누른 순간 일어나도, 버튼을 뗄 때까지 오버레이를 유지해
        // 버튼 떼기 신호가 아래에 있는 다른 프로그램으로 가지 않게 한다.
        if (!_selection.IsFinished || _buttonDown)
        {
            foreach (var o in _overlays) o.Redraw();
            return;
        }

        _finished = true;
        _finder?.Dispose();
        if (_selection.Phase != SelectionPhase.Confirmed) _cursor?.Dispose(); // 취소: 쓰지 않은 커서 복사본 정리
        var overlays = _overlays.ToList();
        _overlays.Clear();
        foreach (var o in overlays) o.Close();

        CaptureResult? result = null;
        try
        {
            if (_selection.Phase == SelectionPhase.Confirmed && _selection.Rect is { IsEmpty: false } rect)
                result = Cut(Image, Layout, rect, _resultMode, _cursor) with { RecordGif = _recordGif };
        }
        catch (Exception ex)
        {
            AppLog.Write($"{_mode} 잘라내기 실패 {_selection.Rect}: {ex}");
        }
        _done.TrySetResult(result);
    }

    private static bool IsCtrlDown() => Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
    private static bool IsShiftDown() => Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
    private static bool IsAltDown() => Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt);
}
