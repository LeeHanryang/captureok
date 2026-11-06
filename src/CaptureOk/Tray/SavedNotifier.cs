using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CaptureOk.Native;
using Microsoft.Win32;

namespace CaptureOk.Tray;

/// <summary>
/// 저장 알림: 화면 오른쪽 아래(작업 표시줄 위)에 잠깐 뜨는 작은 창. "열기"·"폴더 열기" 버튼이 있고, 알림 자체를 눌러도 연다.
/// 창을 띄울 수 없으면 트레이 풍선 알림(클릭 = 열기)으로.
/// </summary>
internal sealed class SavedNotifier(TrayHost tray)
{
    private static readonly TimeSpan Shown = TimeSpan.FromSeconds(6);
    private NoticeWindow? _current;

    public void Show(string title, string body, string path)
    {
        try
        {
            _current?.Close(); // 한 번에 하나만
            _current = new NoticeWindow(title, body, path, Act);
            _current.Closed += (sender, _) => { if (ReferenceEquals(sender, _current)) _current = null; };
            _current.Show();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            AppLog.Write($"저장 알림 창을 띄우지 못함: {ex.Message}");
            tray.ShowFileSaved(title, body, path);
        }
    }

    private void Act(string path, bool folder)
    {
        _current?.Close();
        try
        {
            if (!File.Exists(path))
            {
                tray.ShowBalloon("CaptureOK", $"파일이 없습니다: {Path.GetFileName(path)}");
                return;
            }
            if (folder) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); // 폴더를 열고 그 파일을 고른다
            else Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            AppLog.Write($"알림: {(folder ? "폴더 열기" : "열기")} {path}");
        }
        catch (Exception ex)
        {
            AppLog.Write($"알림 동작 실패: {ex.Message}");
            tray.ShowBalloon("CaptureOK", $"열지 못했습니다: {ex.Message}", System.Windows.Forms.ToolTipIcon.Warning);
        }
    }

    /// <summary>1.5.0이 Windows 알림용으로 등록한 앱 이름·아이콘을 지운다 (이제 쓰지 않음).</summary>
    public static void RemoveOldRegistration()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\AppUserModelId\LeeHanryang.CaptureOK", throwOnMissingSubKey: false);
            var icon = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CaptureOk", "notification-icon.png");
            if (File.Exists(icon)) File.Delete(icon);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            AppLog.Write($"예전 알림 등록을 지우지 못함: {ex.Message}");
        }
    }

    /// <summary>알림 창. 포커스를 가져가지 않고, 캡처에 찍히지 않으며, 마우스를 올려 두면 닫히지 않는다.</summary>
    private sealed class NoticeWindow : Window
    {
        private readonly DispatcherTimer _timer = new() { Interval = Shown };

        public NoticeWindow(string title, string body, string path, Action<string, bool> act)
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            SizeToContent = SizeToContent.Height;
            Width = 340;
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x20, 0x20, 0x20));
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF));
            BorderThickness = new Thickness(1);
            Cursor = Cursors.Hand;

            var close = new Button { Content = "✕", Focusable = false, Padding = new Thickness(8, 2, 8, 2), Cursor = Cursors.Arrow, ToolTip = "닫기" };
            close.Click += (_, _) => Close();
            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(close, Dock.Right);
            header.Children.Add(close);
            header.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });

            var lines = new TextBlock { Text = body, Foreground = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)), FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
            var open = new Button { Content = "열기", Focusable = false, Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Arrow };
            open.Click += (_, e) => { e.Handled = true; act(path, false); };
            var folder = new Button { Content = "폴더 열기", Focusable = false, Padding = new Thickness(16, 4, 16, 4), Cursor = Cursors.Arrow };
            folder.Click += (_, e) => { e.Handled = true; act(path, true); };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            buttons.Children.Add(open);
            buttons.Children.Add(folder);

            var panel = new StackPanel { Margin = new Thickness(14, 10, 10, 12) };
            panel.Children.Add(header);
            panel.Children.Add(lines);
            panel.Children.Add(buttons);
            Content = panel;

            // 버튼이 아닌 곳을 누르면 연다
            MouseLeftButtonUp += (_, e) => { if (!e.Handled && !IsOnButton(e.OriginalSource)) act(path, false); };
            MouseEnter += (_, _) => _timer.Stop();
            MouseLeave += (_, _) => _timer.Start();
            _timer.Tick += (_, _) => Close();
            Closed += (_, _) => _timer.Stop();

            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -10000; // 크기가 정해진 뒤 오른쪽 아래로 옮긴다
            Top = -10000;
            SourceInitialized += (_, _) =>
            {
                var handle = new WindowInteropHelper(this).Handle;
                var ex = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE);
                NativeMethods.SetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE, (nint)(ex | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW));
                NativeMethods.SetWindowDisplayAffinity(handle, NativeMethods.WDA_EXCLUDEFROMCAPTURE); // 다음 캡처에 알림이 찍히지 않게
            };
            Loaded += (_, _) => Place();
            SizeChanged += (_, _) => Place();
            _timer.Start();
        }

        /// <summary>주 모니터 작업 영역(작업 표시줄 제외)의 오른쪽 아래.</summary>
        private void Place()
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth - 12;
            Top = area.Bottom - ActualHeight - 12;
        }

        private static bool IsOnButton(object source)
        {
            for (var e = source as DependencyObject; e is not null; e = e is Visual ? VisualTreeHelper.GetParent(e) : LogicalTreeHelper.GetParent(e))
                if (e is Button) return true;
            return false;
        }
    }
}
