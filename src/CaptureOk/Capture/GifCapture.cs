using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using CaptureOk.Core.Geometry;
using CaptureOk.Core.Imaging;
using CaptureOk.Native;

namespace CaptureOk.Capture;

/// <summary>
/// 고른 영역을 일정한 간격으로 찍어 임시 폴더의 GIF 파일로 바로 써 나간다 (마우스 커서 포함).
/// 녹화하는 동안 화면을 조작할 수 있도록 입력은 가로채지 않고, Esc만 멈춤으로 받는다.
/// </summary>
internal static class GifCapture
{
    public const int FramesPerSecond = 15;
    public static readonly TimeSpan MaxDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan OverlayGone = TimeSpan.FromMilliseconds(120); // 영역을 고른 화면이 사라진 뒤부터 찍는다

    private static string Folder => Path.Combine(Path.GetTempPath(), "CaptureOk", "gif");

    /// <param name="Path">녹화한 임시 파일 (옮기거나 지우는 것은 부른 쪽 몫)</param>
    /// <param name="Notice">녹화가 이용자가 멈추기 전에 끝났으면 그 이유</param>
    public sealed record Result(string Path, TimeSpan Duration, long Bytes, string? Notice);

    public static async Task<Result> RunAsync(PixelRect rect)
    {
        Directory.CreateDirectory(Folder);
        DeleteLeftovers();
        var path = Path.Combine(Folder, $"{Guid.NewGuid():N}.gif");

        StopSignal? stop = null;
        Indicator? indicator = null;
        Task<Result>? recording = null;
        try
        {
            var monitor = ScreenGrabber.CurrentLayout().MonitorAt(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
            stop = new StopSignal();
            indicator = new Indicator(rect, monitor, stop.Request);
            indicator.Show();
            if (!indicator.Placed) indicator.Hide(); // 찍히지 않게 둘 곳이 없으면 Esc로만 멈춘다
            ScrollCapture.FocusTarget(rect); // 영역을 고른 창에 곧바로 키보드 입력이 가고, 그 창이 맨 위에 찍히게
            await Task.Delay(OverlayGone);

            var clock = Stopwatch.StartNew();
            var signal = stop;
            recording = Task.Factory.StartNew(() => Record(rect, path, signal), TaskCreationOptions.LongRunning);
            while (!recording.IsCompleted)
            {
                indicator.Update(clock.Elapsed);
                await Task.WhenAny(recording, Task.Delay(200));
            }
            return await recording;
        }
        catch
        {
            stop?.Request();
            if (recording is not null) await Task.WhenAny(recording); // 파일을 닫을 때까지 기다린 뒤 지운다
            TryDelete(path);
            throw;
        }
        finally
        {
            indicator?.Close();
            if (stop is not null) await stop.CloseAsync();
        }
    }

    private static Result Record(PixelRect rect, string path, StopSignal stop)
    {
        var interval = 1000L / FramesPerSecond;
        string? notice = null;
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16);
        var gif = new GifEncoder(file, rect.Width, rect.Height);
        var buffer = new byte[rect.Width * rect.Height * 4];
        var clock = Stopwatch.StartNew();
        var due = 0L;
        var frames = 0;
        // 곧바로 멈춰도 한 장은 찍는다 — 프레임이 없는 GIF는 열리지 않는다
        while (frames == 0 || !stop.Requested)
        {
            if (clock.Elapsed >= MaxDuration)
            {
                notice = $"최대 {(int)MaxDuration.TotalSeconds}초에 닿아 멈췄습니다";
                break;
            }
            try
            {
                ScreenGrabber.GrabRegionPixels(rect, withCursor: true, buffer);
                gif.Add(buffer, clock.ElapsedMilliseconds);
                frames++;
            }
            catch (Exception ex) when (frames > 0 && ex is InvalidOperationException or OutOfMemoryException)
            {
                // 잠금 화면 등: 그때까지 찍은 것을 남긴다
                AppLog.Write($"GIF 녹화 중 화면 찍기 실패: {ex.Message}");
                notice = "화면을 찍지 못해 멈췄습니다";
                break;
            }
            due += interval;
            var wait = due - clock.ElapsedMilliseconds;
            if (wait > 0) Thread.Sleep((int)wait);
            else due = clock.ElapsedMilliseconds; // 큰 영역이라 밀리면 따라잡으려 하지 않는다 (지연 시간은 실제 시간대로 쓰임)
        }
        var duration = clock.Elapsed;
        gif.Finish(clock.ElapsedMilliseconds);
        AppLog.Write($"GIF 녹화 {rect}: 찍은 {frames}장, 쓴 프레임 {gif.FramesWritten}개, {duration.TotalSeconds:F1}초, {file.Length:N0}바이트");
        return new Result(path, duration, file.Length, notice);
    }

    /// <summary>저장하기 전에 앱이 꺼졌으면 임시 파일이 남는다. 시작할 때와 녹화 전에 지운다.</summary>
    public static void DeleteLeftovers()
    {
        try
        {
            if (!Directory.Exists(Folder)) return;
            foreach (var old in Directory.EnumerateFiles(Folder, "*.gif")) TryDelete(old);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"GIF 임시 폴더를 비우지 못함: {ex.Message}");
        }
    }

    public static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"GIF 임시 파일을 지우지 못함: {ex.Message}");
        }
    }

    /// <summary>멈춤 요청: 멈춤 버튼이나 Esc. Esc만 가로채고, 다른 입력은 녹화 대상 프로그램에 그대로 간다.</summary>
    private sealed class StopSignal
    {
        private readonly NativeMethods.LowLevelHookProc _keyProc;
        private nint _keyHook;
        private volatile bool _requested;
        private volatile bool _escDown;

        public StopSignal()
        {
            _keyProc = OnKey; // 대리자가 수거되지 않게 필드로 붙잡아 둔다
            _keyHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyProc, NativeMethods.GetModuleHandle(null), 0);
            if (_keyHook == 0) AppLog.Write($"GIF 녹화 입력 훅 실패: {Marshal.GetLastWin32Error()}");
        }

        // 키 상태도 함께 읽는다 — 관리자 권한 창이 맨 앞이면 훅에 키가 오지 않는다
        public bool Requested => _requested || (NativeMethods.GetAsyncKeyState(NativeMethods.VK_ESCAPE) & 0x8000) != 0;

        public void Request() => _requested = true;

        private nint OnKey(int code, nint wParam, nint lParam)
        {
            if (code >= 0)
            {
                var info = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                if (info.vkCode == NativeMethods.VK_ESCAPE)
                {
                    if ((int)wParam is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN)
                    {
                        _escDown = true;
                        _requested = true;
                    }
                    else
                    {
                        _escDown = false;
                    }
                    return 1;
                }
            }
            return NativeMethods.CallNextHookEx(_keyHook, code, wParam, lParam);
        }

        /// <summary>Esc를 뗄 때까지(최대 1.5초) 기다렸다가 훅을 푼다 — 누르고 있는 Esc가 저장 창이나 대상 프로그램에 가지 않게.</summary>
        public async Task CloseAsync()
        {
            for (var i = 0; i < 30 && _escDown; i++) await Task.Delay(50);
            if (_keyHook != 0) NativeMethods.UnhookWindowsHookEx(_keyHook);
            _keyHook = 0;
        }
    }

    /// <summary>"GIF 녹화 중" 작은 창과 멈춤 버튼. 영역과 겹치지 않는 곳에 두고, 캡처에 찍히지 않게 한다.</summary>
    private sealed class Indicator : Window
    {
        private readonly TextBlock _text = new()
        {
            Foreground = Brushes.White, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0),
        };

        public Indicator(PixelRect region, MonitorInfo monitor, Action stop)
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Width = 340;
            Height = 40;
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x20, 0x20, 0x20));

            var button = new Button { Content = "멈춤", Focusable = false, Margin = new Thickness(0, 4, 6, 4), Padding = new Thickness(14, 0, 14, 0) };
            button.Click += (_, _) => stop();
            var panel = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(button, Dock.Right);
            panel.Children.Add(button);
            panel.Children.Add(_text);
            Content = panel;
            Update(TimeSpan.Zero);

            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = monitor.Bounds.X / monitor.Scale; // 대략 그 모니터에 띄운 뒤, 물리 픽셀로 정확히 옮긴다
            Top = monitor.Bounds.Y / monitor.Scale;
            var hidden = false;
            void Place()
            {
                var handle = new WindowInteropHelper(this).Handle;
                NativeMethods.GetWindowRect(handle, out var r);
                var place = ScrollPacing.IndicatorPlace(region, monitor.Bounds, r.Right - r.Left, r.Bottom - r.Top, monitor.Scale, hidden);
                NativeMethods.SetWindowPos(handle, NativeMethods.HWND_TOPMOST, place.X, place.Y, 0, 0, NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
                Placed = place.Visible;
            }
            SourceInitialized += (_, _) =>
            {
                var handle = new WindowInteropHelper(this).Handle;
                var ex = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE);
                NativeMethods.SetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE, (nint)(ex | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW));
                hidden = NativeMethods.SetWindowDisplayAffinity(handle, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
                Place();
            };
            // 배율이 다른 모니터로 옮기면 창 크기가 바뀌므로 바뀐 크기로 다시 놓는다
            DpiChanged += (_, _) => Dispatcher.BeginInvoke(Place);
        }

        public bool Placed { get; private set; } = true;

        public void Update(TimeSpan elapsed) =>
            _text.Text = $"GIF 녹화 중 {(int)elapsed.TotalSeconds}초 / {(int)MaxDuration.TotalSeconds}초 — Esc로도 멈춤";
    }
}
