using CaptureOk.Core.Geometry;

namespace CaptureOk.Core.Settings;

public enum ThemeMode { System, Light, Dark }

/// <summary>환경설정 창의 페이지 (정보 페이지는 설정 항목이 없어 빠짐).</summary>
public enum SettingsPage { General, Capture, Hotkeys, Save, FixedSizes, History }

public static class SettingsPages
{
    /// <summary>"이 페이지를 기본값으로": 그 페이지에 보이는 항목만 기본값으로 되돌린다.</summary>
    public static void Reset(CaptureSettings s, SettingsPage page, string userProfile)
    {
        var d = CaptureSettings.CreateDefault(userProfile);
        switch (page)
        {
            case SettingsPage.General:
                s.RunAtStartup = d.RunAtStartup;
                s.Theme = d.Theme;
                break;
            case SettingsPage.Capture:
                s.ConfirmOnRelease = d.ConfirmOnRelease;
                s.AutoDetectElements = d.AutoDetectElements;
                s.IncludeCursor = d.IncludeCursor;
                s.ShowGuides = d.ShowGuides;
                s.ShowMagnifier = d.ShowMagnifier;
                s.ShowCaptureHint = d.ShowCaptureHint;
                break;
            case SettingsPage.Hotkeys:
                s.Hotkeys = d.Hotkeys;
                break;
            case SettingsPage.Save:
                s.SaveFolder = d.SaveFolder;
                s.FileNameTemplate = d.FileNameTemplate;
                s.AutoSave = d.AutoSave;
                break;
            case SettingsPage.FixedSizes:
                s.FixedSizes = d.FixedSizes;
                break;
            case SettingsPage.History:
                s.ShowHistoryPanel = d.ShowHistoryPanel;
                s.HistoryMaxCount = d.HistoryMaxCount;
                s.ConfirmClearHistory = d.ConfirmClearHistory;
                s.ConfirmExitWithUnsaved = d.ConfirmExitWithUnsaved;
                break;
        }
    }
}

/// <summary>고정 크기 캡처의 크기 목록 규칙: 최대 9개, 각 변 1~10000px, 같은 크기는 하나만.</summary>
public static class FixedSizeRules
{
    public const int MaxSide = 10000;

    public static bool TryAdd(List<PixelSize> sizes, PixelSize size)
    {
        if (sizes.Count >= CaptureSettings.MaxFixedSizes) return false;
        if (size.Width is < 1 or > MaxSide || size.Height is < 1 or > MaxSide) return false;
        if (sizes.Contains(size)) return false;
        sizes.Add(size);
        return true;
    }

    /// <summary>index의 항목을 delta(-1 위, +1 아래)만큼 옮긴다. 끝을 넘으면 그대로.</summary>
    public static void Move(List<PixelSize> sizes, int index, int delta)
    {
        var target = index + delta;
        if (index < 0 || index >= sizes.Count || target < 0 || target >= sizes.Count) return;
        (sizes[index], sizes[target]) = (sizes[target], sizes[index]);
    }
}
