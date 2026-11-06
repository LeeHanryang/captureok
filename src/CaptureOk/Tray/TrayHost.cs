using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using CaptureOk.Core.Hotkeys;
using CaptureOk.Core.Settings;
using Timer = System.Windows.Forms.Timer;

namespace CaptureOk.Tray;

/// <summary>
/// 트레이 아이콘과 메뉴: 캡처 방식(오른쪽에 지금 설정된 단축키) ─ 캡처 목록·저장 폴더·환경설정 ─ 종료.
/// 아이콘 한 번 클릭 = 영역 지정 캡처, 더블클릭 = 캡처 목록(편집 창) 열기.
/// </summary>
internal sealed class TrayHost : IDisposable
{
    private static readonly (CaptureMode Mode, string Name)[] CaptureItems =
    [
        (CaptureMode.Region, "영역 지정 캡처"),
        (CaptureMode.Window, "창 캡처"),
        (CaptureMode.Element, "요소 캡처"),
        (CaptureMode.FullScreen, "전체 화면 캡처"),
        (CaptureMode.FixedSize, "고정 크기 캡처"),
        (CaptureMode.Scroll, "스크롤 캡처"),
        (CaptureMode.LastRegion, "직전 영역 다시 캡처"),
    ];

    private readonly NotifyIcon _icon;
    private readonly Icon _iconImage;
    private readonly Timer _singleClickTimer;
    private readonly Dictionary<CaptureMode, ToolStripMenuItem> _captureItems = [];
    private Action? _balloonClicked; // 마지막으로 띄운 알림을 클릭했을 때 할 일

    public TrayHost(Action<CaptureMode> capture, Func<string> saveFolder, Action openEditor, Action openSettings, Action exit)
    {
        _iconImage = CreateIcon();

        var menu = new ContextMenuStrip();
        foreach (var (mode, name) in CaptureItems)
        {
            var item = new ToolStripMenuItem(name, null, (_, _) => capture(mode));
            _captureItems[mode] = item;
            menu.Items.Add(item);
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("캡처 목록 열기", null, (_, _) => openEditor());
        menu.Items.Add("저장 폴더 열기", null, (_, _) => OpenFolder(saveFolder()));
        menu.Items.Add("환경설정", null, (_, _) => openSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => exit());

        _icon = new NotifyIcon { Icon = _iconImage, Text = "CaptureOK", ContextMenuStrip = menu, Visible = true };
        // NotifyIcon은 더블클릭의 첫 클릭에도 MouseClick을 보낸다(Clicks는 항상 0).
        // 그래서 한 번 클릭은 더블클릭 대기 시간만큼 기다렸다가 실행하고, 그 사이 더블클릭이 오면 취소한다.
        _singleClickTimer = new Timer { Interval = SystemInformation.DoubleClickTime };
        _singleClickTimer.Tick += (_, _) => { _singleClickTimer.Stop(); capture(CaptureMode.Region); };
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) { _singleClickTimer.Stop(); _singleClickTimer.Start(); } };
        _icon.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) { _singleClickTimer.Stop(); openEditor(); } };
        _icon.BalloonTipClicked += (_, _) => _balloonClicked?.Invoke();
    }

    /// <summary>메뉴 오른쪽 단축키 표시를 설정에 맞춘다 (방식마다 첫 번째 단축키).</summary>
    public void UpdateShortcuts(IReadOnlyDictionary<CaptureMode, List<string>> hotkeys)
    {
        foreach (var (mode, item) in _captureItems)
            item.ShortcutKeyDisplayString = hotkeys.TryGetValue(mode, out var keys) ? keys.Select(HotkeyGesture.Display).FirstOrDefault(k => k.Length > 0) ?? "" : "";
    }

    public void ShowBalloon(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _balloonClicked = null;
        _icon.ShowBalloonTip(4000, title, text, icon);
    }

    /// <summary>저장 알림 (알림 창을 띄울 수 없을 때): 알림을 클릭하면 그 파일을 Windows 기본 프로그램으로 연다.</summary>
    public void ShowFileSaved(string title, string text, string path)
    {
        _icon.ShowBalloonTip(4000, title, $"{text}\n클릭하면 엽니다", ToolTipIcon.Info);
        _balloonClicked = () => RunMenuAction("저장한 파일 열기", () => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }));
    }

    private void OpenFolder(string folder) => RunMenuAction("저장 폴더 열기", () =>
    {
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    });

    /// <summary>트레이 메뉴 동작의 성공·실패를 기록하고, 실패하면 알림으로 알린다.</summary>
    private void RunMenuAction(string name, Action action)
    {
        try
        {
            action();
            AppLog.Write($"트레이: {name}");
        }
        catch (Exception ex)
        {
            AppLog.Write($"트레이: {name} 실패: {ex}");
            ShowBalloon("CaptureOK", $"{name}에 실패했습니다: {ex.Message}", ToolTipIcon.Warning);
        }
    }

    /// <summary>앱 로고(Assets\CaptureOk.ico)에서 트레이 크기(화면 배율에 따라 16·20·24·32px)에 맞는 그림.</summary>
    private static Icon CreateIcon()
    {
        using var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/CaptureOk.ico")).Stream;
        return LoadIcon(stream, SystemInformation.SmallIconSize.Width);
    }

    internal static Icon LoadIcon(Stream stream, int size) => new(stream, size, size);

    public void Dispose()
    {
        _singleClickTimer.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
        _iconImage.Dispose();
    }
}
