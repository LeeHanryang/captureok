using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CaptureOk.Core.Geometry;
using CaptureOk.Native;

namespace CaptureOk.Capture;

/// <summary>
/// 모니터 하나를 덮는 정지 화면 오버레이. 그리기는 물리 픽셀·가상 화면 좌표로 하고,
/// 입력은 모두 <see cref="CaptureSession"/>으로 넘긴다.
/// </summary>
internal sealed class OverlayWindow : Window
{
    private static readonly Brush DimBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x70, 0, 0, 0)));
    private static readonly Brush LabelBrush = Freeze(new SolidColorBrush(Color.FromArgb(0xCC, 0x20, 0x20, 0x20)));
    private static readonly Pen SelectionPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x30))), 1));
    private static readonly Pen HoverPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x30))), 3));
    private static readonly Pen GuidePen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF))), 1));
    private static readonly Pen MagnifierPen = Freeze(new Pen(Brushes.White, 1));

    private readonly CaptureSession _session;
    private readonly MonitorInfo _monitor;
    private readonly Surface _surface;

    public OverlayWindow(CaptureSession session, MonitorInfo monitor)
    {
        _session = session;
        _monitor = monitor;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        Background = Brushes.Black;
        Cursor = Cursors.Cross;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = monitor.Bounds.X / monitor.Scale;
        Top = monitor.Bounds.Y / monitor.Scale;
        Width = monitor.Bounds.Width / monitor.Scale;
        Height = monitor.Bounds.Height / monitor.Scale;

        _surface = new Surface(this);
        RenderOptions.SetBitmapScalingMode(_surface, BitmapScalingMode.NearestNeighbor);
        Content = _surface;

        SourceInitialized += (_, _) =>
        {
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(SwallowKeyboardMenu);
            CoverMonitor();
        };
        // 다른 배율의 모니터로 옮겨지면 WPF가 창 크기를 배율 비율로 바꾸므로, 그 뒤에 다시 모니터에 맞춘다.
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(CoverMonitor);
        Closed += (_, _) => _session.OnOverlayClosed();
        MouseMove += (_, _) => _session.OnMouseMove();
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount >= 2) _session.OnDoubleClick();
            else { CaptureMouse(); _session.OnMouseDown(); }
        };
        MouseLeftButtonUp += (_, _) => { ReleaseMouseCapture(); _session.OnMouseUp(); };
        MouseRightButtonUp += (_, _) => _session.OnRightClick();
        MouseWheel += (_, e) => _session.OnWheel(e.Delta);
        MouseDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Middle) return;
            CaptureMouse();
            _session.OnMiddleDown();
        };
        MouseUp += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Middle) return;
            ReleaseMouseCapture();
            _session.OnMiddleUp();
        };
        PreviewKeyDown += (_, e) => e.Handled = _session.OnKey(e.Key == Key.System ? e.SystemKey : e.Key);
    }

    /// <summary>WPF의 DIP 변환을 거치지 않고 물리 픽셀로 정확히 모니터를 덮는다.</summary>
    private void CoverMonitor()
    {
        var b = _monitor.Bounds;
        NativeMethods.SetWindowPos(new WindowInteropHelper(this).Handle, NativeMethods.HWND_TOPMOST, b.X, b.Y, b.Width, b.Height,
            NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>Alt·F10만 눌렀다 뗐을 때 Windows가 보내는 "메뉴로 들어가기" 명령인가.</summary>
    internal static bool IsKeyboardMenuActivation(int msg, IntPtr wParam) =>
        msg == WM_SYSCOMMAND && ((int)wParam & 0xFFF0) == SC_KEYMENU;

    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_KEYMENU = 0xF100;

    /// <summary>
    /// 오버레이에는 메뉴가 없는데 Alt만 눌렀다 떼면 Windows가 창을 메뉴 선택 상태로 만들어,
    /// 그동안 마우스 움직임이 오지 않아 확대경·영역이 멈춘다. 그 명령만 버린다 (Alt+F4 닫기는 그대로).
    /// </summary>
    private static IntPtr SwallowKeyboardMenu(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (IsKeyboardMenuActivation(msg, wParam)) handled = true;
        return IntPtr.Zero;
    }

    public bool Covers(int x, int y) => _monitor.Bounds.Contains(x, y);

    public void Redraw() => _surface.InvalidateVisual();

    public void ActivateForInput()
    {
        NativeMethods.SetForegroundWindow(new WindowInteropHelper(this).Handle);
        Activate();
        Focus();
        Keyboard.Focus(this);
    }

    /// <summary>커서 자리에 우클릭 메뉴를 띄운다. 항목을 고르지 않고 닫히면 closed를 부른다.</summary>
    public void ShowMenu(Action recordGif, Action closed)
    {
        ReleaseMouseCapture(); // 왼쪽 버튼을 누른 채 연 경우: 메뉴가 마우스를 받게
        var chosen = false;
        var gif = new MenuItem { Header = "GIF 녹화" };
        gif.Click += (_, _) =>
        {
            chosen = true;
            recordGif();
        };
        var menu = new ContextMenu { PlacementTarget = this, Placement = PlacementMode.MousePoint };
        menu.Items.Add(gif);
        // 메뉴는 항목의 Click보다 먼저 닫힐 수 있으므로 Click이 지나간 뒤에 판단한다
        menu.Closed += (_, _) => Dispatcher.BeginInvoke(() => { if (!chosen) closed(); }, DispatcherPriority.Input);
        menu.IsOpen = true;
    }

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    private sealed class Surface(OverlayWindow owner) : FrameworkElement
    {
        protected override void OnRender(DrawingContext dc)
        {
            var session = owner._session;
            var m = owner._monitor.Bounds;
            var vb = session.Layout.VirtualBounds;
            var s = VisualTreeHelper.GetDpi(this).DpiScaleX;

            dc.PushTransform(new ScaleTransform(1 / s, 1 / s));
            dc.PushTransform(new TranslateTransform(-m.X, -m.Y));

            dc.DrawImage(session.Image, new Rect(vb.X, vb.Y, vb.Width, vb.Height));

            var monitorRect = new Rect(m.X, m.Y, m.Width, m.Height);
            var highlight = session.HighlightRect;
            if (highlight is { IsEmpty: false } h)
            {
                var hole = new Rect(h.X, h.Y, h.Width, h.Height);
                dc.DrawGeometry(DimBrush, null, new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(monitorRect), new RectangleGeometry(hole)));
                // 1px 펜이 픽셀 경계에 맞도록 0.5 안쪽으로 그린다
                var pen = session.IsHoverHighlight ? HoverPen : SelectionPen;
                dc.DrawRectangle(null, pen, new Rect(h.X + pen.Thickness / 2, h.Y + pen.Thickness / 2, Math.Max(0, h.Width - pen.Thickness), Math.Max(0, h.Height - pen.Thickness)));
                if (session.ShowHighlightSize) DrawLabel(dc, $"{h.Width} × {h.Height}", h.X, h.Y, s, m);
            }
            else
            {
                dc.DrawRectangle(DimBrush, null, monitorRect);
            }

            var (cx, cy) = session.CursorPosition;
            if (owner.Covers(cx, cy))
            {
                if (session.ShowGuides)
                {
                    dc.DrawLine(GuidePen, new Point(m.X, cy + 0.5), new Point(m.Right, cy + 0.5));
                    dc.DrawLine(GuidePen, new Point(cx + 0.5, m.Y), new Point(cx + 0.5, m.Bottom));
                }
                if (session.ShowMagnifier) Magnifier.Draw(dc, session.Image, vb, cx, cy, s, m, MagnifierPen, LabelBrush);
                if (session.Hint is { } hint) DrawHint(dc, hint, cy, s, m);
            }

            dc.Pop();
            dc.Pop();
        }

        /// <summary>조작 안내: 모니터 위 가운데, 커서가 그 근처에 있으면 아래 가운데 (작업 표시줄 위).</summary>
        private static void DrawHint(DrawingContext dc, string text, int cursorY, double s, PixelRect monitor)
        {
            var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 13 * s, Brushes.White, 1.0);
            ft.MaxTextWidth = monitor.Width - 48 * s;
            var padX = 14 * s;
            var padY = 7 * s;
            var w = ft.Width + padX * 2;
            var h = ft.Height + padY * 2;
            var left = monitor.X + (monitor.Width - w) / 2;
            var top = monitor.Y + 12 * s;
            if (cursorY < top + h + 24 * s) top = monitor.Bottom - h - 56 * s;
            dc.DrawRoundedRectangle(LabelBrush, null, new Rect(left, top, w, h), 6 * s, 6 * s);
            dc.DrawText(ft, new Point(left + padX, top + padY));
        }

        private static void DrawLabel(DrawingContext dc, string text, int x, int y, double s, PixelRect monitor)
        {
            var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12 * s, Brushes.White, 1.0);
            var pad = 4 * s;
            var w = ft.Width + pad * 2;
            var h = ft.Height + pad;
            var top = y - h - 2 * s >= monitor.Y ? y - h - 2 * s : y + 2 * s;
            var left = Math.Min(x, monitor.Right - w);
            dc.DrawRoundedRectangle(LabelBrush, null, new Rect(left, top, w, h), 3 * s, 3 * s);
            dc.DrawText(ft, new Point(left + pad, top + pad / 2));
        }
    }
}
