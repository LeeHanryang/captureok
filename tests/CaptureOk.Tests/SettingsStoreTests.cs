using CaptureOk.Core.Geometry;
using CaptureOk.Core.Settings;

namespace CaptureOk.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private const string UserProfile = @"C:\Users\tester";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "captureok-tests", Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_dir, "settings.json");

    public SettingsStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact(DisplayName = "missing-file-gives-defaults")]
    public void MissingFileGivesDefaults()
    {
        var settings = new SettingsStore(SettingsPath).Load(UserProfile);

        Assert.Equal(@"C:\Users\tester\Pictures\CaptureOk", settings.SaveFolder);
        Assert.Equal("%Y%m%d_%H%M%S", settings.FileNameTemplate);
        // 기본 단축키: Ctrl+Alt + 캡처 방식 영어 이름의 첫 글자 (앱의 Ctrl+Shift 단축키와 겹치지 않게)
        Assert.Equal(["PrintScreen", "Ctrl+Alt+R"], settings.Hotkeys[CaptureMode.Region]);
        Assert.Equal(["Ctrl+Alt+W"], settings.Hotkeys[CaptureMode.Window]);
        Assert.Equal(["Ctrl+Alt+E"], settings.Hotkeys[CaptureMode.Element]);
        Assert.Equal(["Ctrl+Alt+F"], settings.Hotkeys[CaptureMode.FullScreen]);
        Assert.Equal(["Ctrl+Alt+Z"], settings.Hotkeys[CaptureMode.FixedSize]);
        Assert.Equal(["Ctrl+Alt+S"], settings.Hotkeys[CaptureMode.Scroll]);
        Assert.Equal(["Ctrl+Alt+L"], settings.Hotkeys[CaptureMode.LastRegion]);
        Assert.Equal(new PixelSize(800, 450), CaptureSettings.DefaultLastRegion); // 직전 영역이 없을 때 주 모니터 가운데
        Assert.False(settings.RunAtStartup);
        Assert.Equal([new PixelSize(1280, 720)], settings.FixedSizes);
        Assert.Equal(0xFFFF0000u, settings.PenColorArgb);
        Assert.Equal(3, settings.PenWidth);
        Assert.True(settings.ConfirmOnRelease);
        Assert.False(settings.AutoSave);
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact(DisplayName = "round-trip")]
    public void RoundTrip()
    {
        var store = new SettingsStore(SettingsPath);
        var settings = CaptureSettings.CreateDefault(UserProfile);
        settings.SaveFolder = @"D:\shots";
        settings.RunAtStartup = true;
        settings.LastRegion = new PixelRect(-100, 20, 300, 200);
        settings.Hotkeys[CaptureMode.FullScreen] = ["Ctrl+Alt+A"];
        store.Save(settings);

        var loaded = store.Load(UserProfile);

        Assert.Equal(@"D:\shots", loaded.SaveFolder);
        Assert.True(loaded.RunAtStartup);
        Assert.Equal(new PixelRect(-100, 20, 300, 200), loaded.LastRegion);
        Assert.Equal(["Ctrl+Alt+A"], loaded.Hotkeys[CaptureMode.FullScreen]);
    }

    [Fact(DisplayName = "corrupt-file")]
    public void CorruptFile()
    {
        File.WriteAllText(SettingsPath, "{not json");

        var settings = new SettingsStore(SettingsPath).Load(UserProfile);

        Assert.Equal(["PrintScreen", "Ctrl+Alt+R"], settings.Hotkeys[CaptureMode.Region]);
        Assert.Equal("{not json", File.ReadAllText(SettingsPath + ".bak"));
    }

    [Fact(DisplayName = "invalid-hotkey-entry")]
    public void InvalidHotkeyEntry()
    {
        var store = new SettingsStore(SettingsPath);
        var settings = CaptureSettings.CreateDefault(UserProfile);
        settings.Hotkeys[CaptureMode.Window] = ["Ctrl+"];
        settings.Hotkeys[CaptureMode.FullScreen] = ["Ctrl+Alt+A"];
        store.Save(settings);

        var loaded = store.Load(UserProfile);

        Assert.Equal(["Ctrl+Alt+W"], loaded.Hotkeys[CaptureMode.Window]); // 잘못된 값은 기본값으로
        Assert.Equal(["Ctrl+Alt+A"], loaded.Hotkeys[CaptureMode.FullScreen]);
    }

    [Fact(DisplayName = "update-preserves-external-edits")]
    public void UpdatePreservesExternalEdits()
    {
        var store = new SettingsStore(SettingsPath);
        store.Save(CaptureSettings.CreateDefault(UserProfile));

        // 사용자가 앱 실행 중에 settings.json을 직접 고침
        var edited = store.Load(UserProfile);
        edited.SaveFolder = @"E:\edited";
        store.Save(edited);

        // 앱은 직전 영역만 바꿔 저장한다
        var result = store.Update(UserProfile, s => s.LastRegion = new PixelRect(1, 2, 3, 4));

        var loaded = store.Load(UserProfile);
        Assert.Equal(@"E:\edited", loaded.SaveFolder);
        Assert.Equal(new PixelRect(1, 2, 3, 4), loaded.LastRegion);
        Assert.Equal(@"E:\edited", result.SaveFolder);
    }

    [Fact(DisplayName = "history-settings-defaults")]
    public void HistorySettingsDefaults()
    {
        var settings = new SettingsStore(SettingsPath).Load(UserProfile);
        Assert.True(settings.ShowHistoryPanel);
        Assert.Equal(30, settings.HistoryMaxCount);
        Assert.True(settings.ConfirmExitWithUnsaved);
    }

    [Theory(DisplayName = "history-max-count-clamped")]
    [InlineData(1, 10)]   // 고정할 수 있는 개수(10)보다 적게는 줄이지 않는다
    [InlineData(500, 100)]
    [InlineData(40, 40)]
    public void HistoryMaxCountClamped(int stored, int expected)
    {
        var store = new SettingsStore(SettingsPath);
        var settings = CaptureSettings.CreateDefault(UserProfile);
        settings.HistoryMaxCount = stored;
        store.Save(settings);

        Assert.Equal(expected, store.Load(UserProfile).HistoryMaxCount);
    }

    [Fact(DisplayName = "fixed-sizes-capped-at-9")]
    public void FixedSizesCappedAt9()
    {
        var store = new SettingsStore(SettingsPath);
        var settings = CaptureSettings.CreateDefault(UserProfile);
        settings.FixedSizes = Enumerable.Range(1, 12).Select(i => new PixelSize(i * 100, i * 50)).ToList();
        store.Save(settings);

        var loaded = store.Load(UserProfile);

        Assert.Equal(9, loaded.FixedSizes.Count);
        Assert.Equal(new PixelSize(100, 50), loaded.FixedSizes[0]);
        Assert.Equal(new PixelSize(600, 300), loaded.FixedSizes[5]);
    }
    [Theory(DisplayName = "history-panel-width-default-and-clamp")]
    [InlineData(null, 200)]
    [InlineData(100, 150)]
    [InlineData(900, 480)]
    [InlineData(260, 260)]
    public void HistoryPanelWidthDefaultAndClamp(int? stored, int expected)
    {
        var store = new SettingsStore(SettingsPath);
        var settings = CaptureSettings.CreateDefault(UserProfile);
        if (stored is { } w) settings.HistoryPanelWidth = w;
        store.Save(settings);

        Assert.Equal(expected, store.Load(UserProfile).HistoryPanelWidth);
    }

    [Fact(DisplayName = "confirm-clear-history-default-true")]
    public void ConfirmClearHistoryDefaultTrue() =>
        Assert.True(new SettingsStore(SettingsPath).Load(UserProfile).ConfirmClearHistory);

    [Fact(DisplayName = "undefined-theme-becomes-system")]
    public void UndefinedThemeBecomesSystem()
    {
        var store = new SettingsStore(SettingsPath);
        var settings = CaptureSettings.CreateDefault(UserProfile);
        settings.Theme = (ThemeMode)42;
        store.Save(settings);

        Assert.Equal(ThemeMode.System, store.Load(UserProfile).Theme);
    }

    [Fact(DisplayName = "try-load-reports-corrupt-file")]
    public void TryLoadReportsCorruptFile()
    {
        var store = new SettingsStore(SettingsPath);
        Assert.True(store.TryLoad(UserProfile, out _)); // 파일이 없으면 기본값으로 성공

        File.WriteAllText(SettingsPath, "{ 고치다 만 json");
        Assert.False(store.TryLoad(UserProfile, out var settings));
        Assert.NotNull(settings); // 그래도 쓸 수 있는 기본값은 준다
    }

    [Fact(DisplayName = "clone-is-independent")]
    public void CloneIsIndependent()
    {
        var a = CaptureSettings.CreateDefault(UserProfile);
        var b = a.Clone();
        b.Hotkeys[CaptureMode.Window] = ["Ctrl+Alt+Q"];
        b.FixedSizes.Add(new CaptureOk.Core.Geometry.PixelSize(10, 10));
        b.Theme = ThemeMode.Dark;

        Assert.Equal(["Ctrl+Alt+W"], a.Hotkeys[CaptureMode.Window]);
        Assert.Single(a.FixedSizes);
        Assert.Equal(ThemeMode.System, a.Theme);
    }
}