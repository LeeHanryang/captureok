using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using CaptureOk.Core.Files;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace CaptureOk.Tray;

/// <summary>
/// 저장 알림: Windows 알림에 "열기"·"폴더 열기" 버튼을 붙여 띄운다. Windows 알림을 띄울 수 없으면 트레이 풍선 알림(클릭 = 열기)으로.
/// 설치하지 않은 앱도 알림을 띄울 수 있도록 앱 식별자를 사용자 레지스트리에 등록한다 (제거 프로그램이 지운다).
/// 누른 버튼은 앱이 켜져 있는 동안에만 처리된다.
/// </summary>
internal sealed class SavedNotifier(TrayHost tray, Dispatcher ui)
{
    public const string AppUserModelId = "LeeHanryang.CaptureOK";
    private const int KeepShown = 20;

    private readonly List<ToastNotification> _shown = []; // 알림 센터에서 나중에 눌러도 처리하도록 붙잡아 둔다
    private ToastNotifier? _notifier;
    private bool _unavailable;

    public void Show(string title, string body, string path)
    {
        if (_unavailable || !TryToast(title, body, path)) tray.ShowFileSaved(title, body, path);
    }

    private bool TryToast(string title, string body, string path)
    {
        try
        {
            if (_notifier is null)
            {
                Register();
                _notifier = ToastNotificationManager.CreateToastNotifier(AppUserModelId);
            }
            if (_notifier.Setting != NotificationSetting.Enabled)
            {
                AppLog.Write($"Windows 알림이 꺼져 있음 ({_notifier.Setting}), 트레이 알림으로");
                return false;
            }
            var xml = new XmlDocument();
            xml.LoadXml(SavedToast.Xml(title, body));
            var toast = new ToastNotification(xml);
            toast.Activated += (_, args) =>
            {
                var argument = (args as ToastActivatedEventArgs)?.Arguments ?? "";
                ui.BeginInvoke(() => Act(SavedToast.ActionOf(argument), path));
            };
            toast.Failed += (_, args) =>
            {
                AppLog.Write($"Windows 알림 실패: {args.ErrorCode?.Message}");
                ui.BeginInvoke(() =>
                {
                    _unavailable = true;
                    tray.ShowFileSaved(title, body, path);
                });
            };
            _shown.Add(toast);
            if (_shown.Count > KeepShown) _shown.RemoveAt(0);
            _notifier.Show(toast);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write($"Windows 알림을 쓸 수 없음: {ex.GetType().Name}: {ex.Message}");
            _unavailable = true;
            return false;
        }
    }

    private void Act(SavedToastAction action, string path)
    {
        if (action == SavedToastAction.None) return;
        try
        {
            if (!File.Exists(path))
            {
                tray.ShowBalloon("CaptureOK", $"파일이 없습니다: {Path.GetFileName(path)}");
                return;
            }
            if (action == SavedToastAction.Open) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            else Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); // 폴더를 열고 그 파일을 고른다
            AppLog.Write($"알림: {(action == SavedToastAction.Open ? "열기" : "폴더 열기")} {path}");
        }
        catch (Exception ex)
        {
            AppLog.Write($"알림 동작 실패: {ex.Message}");
            tray.ShowBalloon("CaptureOK", $"열지 못했습니다: {ex.Message}", System.Windows.Forms.ToolTipIcon.Warning);
        }
    }

    /// <summary>알림에 보일 앱 이름과 아이콘. 아이콘은 앱 안의 로고를 사용자 폴더에 꺼내 둔다.</summary>
    private static void Register()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CaptureOk");
        Directory.CreateDirectory(folder);
        var icon = Path.Combine(folder, "notification-icon.png");
        using (var logo = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/logo.png")).Stream)
        using (var file = File.Create(icon))
            logo.CopyTo(file);
        using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{AppUserModelId}");
        key.SetValue("DisplayName", "CaptureOK");
        key.SetValue("IconUri", icon);
    }
}
