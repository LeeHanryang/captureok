using System.Text.Json;
using System.Text.Json.Serialization;
using CaptureOk.Core.Hotkeys;

namespace CaptureOk.Core.Settings;

/// <summary>settings.json 불러오기·저장. 깨진 파일은 .bak으로 보존하고 기본값으로 시작한다.</summary>
public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string Path { get; } = path;

    public CaptureSettings Load(string userProfile)
    {
        TryLoad(userProfile, out var settings);
        return settings;
    }

    /// <summary>
    /// 설정을 읽는다. 파일이 깨졌으면 .bak으로 보존하고 false (settings에는 기본값) — 앱 실행 중 다시 읽을 때는
    /// 이를 보고 지금 설정을 유지한다 (고치다 만 파일 때문에 자동 실행·단축키가 기본값으로 바뀌지 않게).
    /// </summary>
    public bool TryLoad(string userProfile, out CaptureSettings settings)
    {
        if (!File.Exists(Path))
        {
            settings = CaptureSettings.CreateDefault(userProfile);
            return true;
        }

        CaptureSettings? loaded;
        try
        {
            loaded = JsonSerializer.Deserialize<CaptureSettings>(File.ReadAllText(Path), JsonOptions);
        }
        catch (JsonException)
        {
            File.Copy(Path, Path + ".bak", overwrite: true);
            settings = CaptureSettings.CreateDefault(userProfile);
            return false;
        }

        settings = Normalize(loaded ?? CaptureSettings.CreateDefault(userProfile), userProfile);
        return true;
    }

    /// <summary>
    /// 디스크의 최신 설정을 읽어 일부만 바꿔 저장한다. 앱 실행 중 사용자가 settings.json을 고쳐도 덮어쓰지 않는다.
    /// 파일이 깨져 있으면 fallback(앱이 쓰고 있는 설정)에 바꿔 저장한다 — 기본값으로 덮지 않게.
    /// </summary>
    public CaptureSettings Update(string userProfile, Action<CaptureSettings> change, CaptureSettings? fallback = null)
    {
        if (!TryLoad(userProfile, out var settings) && fallback is not null) settings = fallback.Clone();
        change(settings);
        Save(settings);
        return settings;
    }

    internal static string Serialize(CaptureSettings settings) => JsonSerializer.Serialize(settings, JsonOptions);

    internal static CaptureSettings Deserialize(string json) =>
        JsonSerializer.Deserialize<CaptureSettings>(json, JsonOptions) ?? throw new JsonException("빈 설정");

    public void Save(CaptureSettings settings)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temp, Path, overwrite: true);
    }

    private static CaptureSettings Normalize(CaptureSettings s, string userProfile)
    {
        if (string.IsNullOrWhiteSpace(s.SaveFolder)) s.SaveFolder = CaptureSettings.DefaultSaveFolder(userProfile);
        if (string.IsNullOrWhiteSpace(s.FileNameTemplate)) s.FileNameTemplate = "%Y%m%d_%H%M%S";
        if (s.PenWidth < 1) s.PenWidth = 3;
        s.HistoryMaxCount = Math.Clamp(s.HistoryMaxCount, CaptureSettings.MinHistoryCount, CaptureSettings.MaxHistoryCount);
        if (!Enum.IsDefined(s.Theme)) s.Theme = ThemeMode.System;
        s.HistoryPanelWidth = Math.Clamp(s.HistoryPanelWidth, CaptureSettings.MinHistoryPanelWidth, CaptureSettings.MaxHistoryPanelWidth);

        // 잘못된 단축키가 하나라도 있는 모드는 그 모드만 기본값으로 되돌린다.
        var defaults = CaptureSettings.DefaultHotkeys();
        s.Hotkeys ??= [];
        foreach (var (mode, defaultKeys) in defaults)
        {
            if (!s.Hotkeys.TryGetValue(mode, out var keys) || keys is null || keys.Any(k => !HotkeyGesture.TryParse(k, out _)))
                s.Hotkeys[mode] = defaultKeys;
        }

        s.FixedSizes = (s.FixedSizes ?? []).Where(f => f.Width > 0 && f.Height > 0).Take(CaptureSettings.MaxFixedSizes).ToList();
        if (s.FixedSizes.Count == 0) s.FixedSizes = CaptureSettings.DefaultFixedSizes();
        s.ExcludedWindows = (s.ExcludedWindows ?? []).Where(r => r is not null).ToList();
        return s;
    }
}
