using CaptureOk.Core.Geometry;

namespace CaptureOk.Core.Settings;

public sealed class CaptureSettings
{
    /// <summary>고정 크기 캡처의 크기 목록 최대 개수 — 오버레이에서 숫자 1~9로 고른다.</summary>
    public const int MaxFixedSizes = 9;
    /// <summary>캡처 목록 최대 개수의 아래 한도 — 고정할 수 있는 개수만큼은 담는다.</summary>
    public const int MinHistoryCount = CaptureOk.Core.History.CaptureHistory.MaxPinned;
    public const int MaxHistoryCount = 100;

    public string SaveFolder { get; set; } = "";
    public string FileNameTemplate { get; set; } = "%Y%m%d_%H%M%S";
    public Dictionary<CaptureMode, List<string>> Hotkeys { get; set; } = DefaultHotkeys();
    public bool RunAtStartup { get; set; }
    public PixelRect? LastRegion { get; set; }
    public List<PixelSize> FixedSizes { get; set; } = DefaultFixedSizes();
    public uint PenColorArgb { get; set; } = 0xFFFF0000;
    public int PenWidth { get; set; } = 3;

    /// <summary>false(기본): 저장할 때 위치·이름을 고르는 창을 띄우고, 고른 폴더를 SaveFolder로 기억한다. true: SaveFolder에 자동 이름으로 바로 저장.</summary>
    public bool AutoSave { get; set; }

    /// <summary>편집 창 오른쪽 캡처 목록 패널을 보일지.</summary>
    public bool ShowHistoryPanel { get; set; } = true;

    /// <summary>최근 캡처 목록 최대 개수 (5~100).</summary>
    public int HistoryMaxCount { get; set; } = 30;

    /// <summary>캡처 목록 패널 폭 (DIP, 150~480). 편집 영역과의 구분선을 끌어 바꾼다.</summary>
    public int HistoryPanelWidth { get; set; } = DefaultHistoryPanelWidth;

    /// <summary>목록 비우기 전에 확인할지.</summary>
    public bool ConfirmClearHistory { get; set; } = true;

    public const int DefaultHistoryPanelWidth = 200, MinHistoryPanelWidth = 150, MaxHistoryPanelWidth = 480;

    /// <summary>종료할 때 저장·복사하지 않은 캡처가 있으면 확인할지.</summary>
    public bool ConfirmExitWithUnsaved { get; set; } = true;

    /// <summary>화면 테마: Windows 따라가기(기본)·밝게·어둡게.</summary>
    public ThemeMode Theme { get; set; } = ThemeMode.System;

    /// <summary>캡처한 이미지에 마우스 커서를 넣을지.</summary>
    public bool IncludeCursor { get; set; }

    /// <summary>영역 지정에서 마우스 아래 창·화면 요소를 자동으로 강조하고 클릭으로 고를지.</summary>
    public bool AutoDetectElements { get; set; } = true;

    /// <summary>자동 감지·창 캡처에서 뺄 창 (설정 파일에서 추가). 기본은 비어 있음.</summary>
    public List<CaptureOk.Core.Windows.ExclusionRule> ExcludedWindows { get; set; } = [];

    /// <summary>영역을 고를 때 십자 안내선을 보일지.</summary>
    public bool ShowGuides { get; set; } = true;

    /// <summary>영역을 고를 때 커서 옆 돋보기를 보일지.</summary>
    public bool ShowMagnifier { get; set; } = true;

    /// <summary>영역을 고를 때 화면에 조작 안내(클릭·드래그·휠 클릭·우클릭)를 보일지.</summary>
    public bool ShowCaptureHint { get; set; } = true;

    /// <summary>true(기본): 드래그를 놓으면 바로 캡처. false: 놓은 뒤 방향키로 조정하고 Enter·더블클릭으로 캡처.</summary>
    public bool ConfirmOnRelease { get; set; } = true;

    /// <summary>깊은 복사 (목록·사전까지 따로). 바꾸기 전 값을 비교용으로 남길 때.</summary>
    public CaptureSettings Clone() => SettingsStore.Deserialize(SettingsStore.Serialize(this));

    public static CaptureSettings CreateDefault(string userProfile) => new()
    {
        SaveFolder = DefaultSaveFolder(userProfile),
    };

    public static string DefaultSaveFolder(string userProfile) => Path.Combine(userProfile, "Pictures", "CaptureOk");

    public static Dictionary<CaptureMode, List<string>> DefaultHotkeys() => new()
    {
        // Ctrl+Alt + 캡처 방식 영어 이름의 첫 글자: Region·Window·Element·Full screen·siZe·Scroll·Last.
        // 앱들이 많이 쓰는 Ctrl+Shift 단축키(다른 이름으로 저장·새로 고침 등)와 겹치지 않게 Ctrl+Alt로.
        [CaptureMode.Region] = ["PrintScreen", "Ctrl+Alt+R"],
        [CaptureMode.Window] = ["Ctrl+Alt+W"],
        [CaptureMode.Element] = ["Ctrl+Alt+E"],
        [CaptureMode.FullScreen] = ["Ctrl+Alt+F"],
        [CaptureMode.Scroll] = ["Ctrl+Alt+S"],
        [CaptureMode.FixedSize] = ["Ctrl+Alt+Z"],
        [CaptureMode.LastRegion] = ["Ctrl+Alt+L"],
    };

    public static List<PixelSize> DefaultFixedSizes() => [new PixelSize(1280, 720)];

    /// <summary>직전 영역이 아직 없을 때 주 모니터 가운데에 쓰는 크기 (16:9).</summary>
    public static readonly PixelSize DefaultLastRegion = new(800, 450);
}
