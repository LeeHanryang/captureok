using CaptureOk.Core.Geometry;
using CaptureOk.Core.Hotkeys;
using CaptureOk.Core.Settings;

namespace CaptureOk.Tests;

public class SettingsPagesTests
{
    private const string UserProfile = @"C:\Users\tester";

    /// <summary>모든 항목을 기본값과 다르게 바꾼 설정.</summary>
    private static CaptureSettings Changed()
    {
        var s = CaptureSettings.CreateDefault(UserProfile);
        s.RunAtStartup = true;
        s.Theme = ThemeMode.Dark;
        s.ConfirmOnRelease = false;
        s.IncludeCursor = true;
        s.ShowGuides = false;
        s.ShowMagnifier = false;
        s.ShowCaptureHint = false;
        s.Hotkeys[CaptureMode.Window] = ["Ctrl+Alt+W"];
        s.SaveFolder = @"D:\shots";
        s.FileNameTemplate = "shot_%H%M";
        s.AutoSave = true;
        s.FixedSizes = [new PixelSize(800, 600)];
        s.ShowHistoryPanel = false;
        s.HistoryMaxCount = 10;
        s.ConfirmClearHistory = false;
        s.ConfirmExitWithUnsaved = false;
        s.PenWidth = 8;
        return s;
    }

    [Fact(DisplayName = "new-settings-defaults")]
    public void NewSettingsDefaults()
    {
        var s = CaptureSettings.CreateDefault(UserProfile);
        Assert.Equal(ThemeMode.System, s.Theme);
        Assert.False(s.IncludeCursor);
        Assert.True(s.ShowGuides);
        Assert.True(s.ShowMagnifier);
        Assert.True(s.ShowCaptureHint);
    }

    [Theory(DisplayName = "reset-page-touches-only-its-fields")]
    [InlineData(SettingsPage.General)]
    [InlineData(SettingsPage.Capture)]
    [InlineData(SettingsPage.Hotkeys)]
    [InlineData(SettingsPage.Save)]
    [InlineData(SettingsPage.FixedSizes)]
    [InlineData(SettingsPage.History)]
    public void ResetPageTouchesOnlyItsFields(SettingsPage page)
    {
        var s = Changed();
        SettingsPages.Reset(s, page, UserProfile);
        var d = CaptureSettings.CreateDefault(UserProfile);
        var c = Changed();

        bool On(SettingsPage p) => p == page;
        Assert.Equal(On(SettingsPage.General) ? d.RunAtStartup : c.RunAtStartup, s.RunAtStartup);
        Assert.Equal(On(SettingsPage.General) ? d.Theme : c.Theme, s.Theme);
        Assert.Equal(On(SettingsPage.Capture) ? d.ConfirmOnRelease : c.ConfirmOnRelease, s.ConfirmOnRelease);
        Assert.Equal(On(SettingsPage.Capture) ? d.IncludeCursor : c.IncludeCursor, s.IncludeCursor);
        Assert.Equal(On(SettingsPage.Capture) ? d.ShowGuides : c.ShowGuides, s.ShowGuides);
        Assert.Equal(On(SettingsPage.Capture) ? d.ShowMagnifier : c.ShowMagnifier, s.ShowMagnifier);
        Assert.Equal(On(SettingsPage.Capture) ? d.ShowCaptureHint : c.ShowCaptureHint, s.ShowCaptureHint);
        Assert.Equal(On(SettingsPage.Hotkeys) ? d.Hotkeys[CaptureMode.Window] : c.Hotkeys[CaptureMode.Window], s.Hotkeys[CaptureMode.Window]);
        Assert.Equal(On(SettingsPage.Save) ? d.SaveFolder : c.SaveFolder, s.SaveFolder);
        Assert.Equal(On(SettingsPage.Save) ? d.FileNameTemplate : c.FileNameTemplate, s.FileNameTemplate);
        Assert.Equal(On(SettingsPage.Save) ? d.AutoSave : c.AutoSave, s.AutoSave);
        Assert.Equal(On(SettingsPage.FixedSizes) ? d.FixedSizes : c.FixedSizes, s.FixedSizes);
        Assert.Equal(On(SettingsPage.History) ? d.ShowHistoryPanel : c.ShowHistoryPanel, s.ShowHistoryPanel);
        Assert.Equal(On(SettingsPage.History) ? d.HistoryMaxCount : c.HistoryMaxCount, s.HistoryMaxCount);
        Assert.Equal(On(SettingsPage.History) ? d.ConfirmClearHistory : c.ConfirmClearHistory, s.ConfirmClearHistory);
        Assert.Equal(On(SettingsPage.History) ? d.ConfirmExitWithUnsaved : c.ConfirmExitWithUnsaved, s.ConfirmExitWithUnsaved);
        Assert.Equal(c.PenWidth, s.PenWidth); // 페이지에 없는 항목은 그대로
    }

    [Fact(DisplayName = "duplicates-are-reported-per-mode")]
    public void DuplicatesAreReportedPerMode()
    {
        var map = CaptureSettings.DefaultHotkeys();
        map[CaptureMode.Window] = ["ctrl + alt + f"]; // 전체 화면(Ctrl+Alt+F)과 같은 키
        var dups = HotkeyConflicts.Duplicates(map);

        Assert.Equal([CaptureMode.Window, CaptureMode.FullScreen], dups.Keys.Order()); // enum 선언 순서
        Assert.Equal(["Ctrl+Alt+F"], dups[CaptureMode.FullScreen]);
        Assert.Equal(["Ctrl+Alt+F"], dups[CaptureMode.Window]);
        Assert.Empty(HotkeyConflicts.Duplicates(CaptureSettings.DefaultHotkeys()));
    }

    [Theory(DisplayName = "display-shortens-printscreen")]
    [InlineData("PrintScreen", "PrtSc")]
    [InlineData("Ctrl+PrintScreen", "Ctrl+PrtSc")]
    [InlineData("ctrl+shift+c", "Ctrl+Shift+C")]
    [InlineData("잘못된 키", "")]
    public void DisplayShortensPrintScreen(string gesture, string expected) =>
        Assert.Equal(expected, HotkeyGesture.Display(gesture));

    [Fact(DisplayName = "fixed-size-add-rules")]
    public void FixedSizeAddRules()
    {
        var sizes = new List<PixelSize> { new(1280, 720) };
        Assert.True(FixedSizeRules.TryAdd(sizes, new PixelSize(800, 600)));
        Assert.False(FixedSizeRules.TryAdd(sizes, new PixelSize(800, 600)));   // 중복
        Assert.False(FixedSizeRules.TryAdd(sizes, new PixelSize(0, 600)));     // 0
        Assert.False(FixedSizeRules.TryAdd(sizes, new PixelSize(10001, 600))); // 너무 큼
        foreach (var w in new[] { 100, 200, 300, 400, 500, 600, 700 }) Assert.True(FixedSizeRules.TryAdd(sizes, new PixelSize(w, w)));
        Assert.Equal(9, sizes.Count); // 오버레이에서 숫자 1~9로 고른다
        Assert.False(FixedSizeRules.TryAdd(sizes, new PixelSize(900, 900)));   // 10번째
    }

    [Fact(DisplayName = "fixed-size-move")]
    public void FixedSizeMove()
    {
        PixelSize a = new(1, 1), b = new(2, 2), c = new(3, 3);
        var sizes = new List<PixelSize> { a, b, c };
        FixedSizeRules.Move(sizes, 2, -1);
        Assert.Equal([a, c, b], sizes);
        FixedSizeRules.Move(sizes, 0, -1); // 맨 위에서 위로는 그대로
        Assert.Equal([a, c, b], sizes);
        FixedSizeRules.Move(sizes, 2, +1); // 맨 아래에서 아래로는 그대로
        Assert.Equal([a, c, b], sizes);
    }
}
