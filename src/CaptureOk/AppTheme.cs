using System.Windows;
using System.Windows.Media;
using CaptureOk.Core.Settings;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using ThemeMode = CaptureOk.Core.Settings.ThemeMode;

namespace CaptureOk;

/// <summary>
/// 테마: Windows 앱 모드 따라가기(바뀌면 바로 따라감) 또는 밝게·어둡게 고정.
/// 강조색은 Windows 강조색(회색 등으로 바꿔 둔 경우 어색함) 대신 앱 고유색을 쓰고,
/// 눌린 토글(선택된 도구·목록)은 강조색으로 칠하지 않고 한 단계 밝은(라이트는 어두운) 무채색 배경만 준다.
/// </summary>
internal static class AppTheme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private static readonly Color Accent = Color.FromRgb(0x00, 0x78, 0xD4);
    private static ThemeMode _mode = ThemeMode.System;
    private static bool _watching;

    public static void Apply(ThemeMode mode)
    {
        _mode = mode;
        ApplyCurrent();
        if (_watching) return;
        _watching = true;
        ApplicationThemeManager.Changed += (current, _) => ApplyAppColors(current);
        // Windows 테마가 바뀌면 "Windows 따라가기"일 때만 다시 적용한다 (고정이면 그대로).
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category != UserPreferenceCategory.General) return;
            Application.Current?.Dispatcher.BeginInvoke(() => { if (_mode == ThemeMode.System) ApplyCurrent(); });
        };
    }

    /// <summary>설정과 Windows 앱 모드로 실제 테마를 정한다.</summary>
    internal static ApplicationTheme Resolve(ThemeMode mode, bool systemPrefersLight) => mode switch
    {
        ThemeMode.Light => ApplicationTheme.Light,
        ThemeMode.Dark => ApplicationTheme.Dark,
        _ => systemPrefersLight ? ApplicationTheme.Light : ApplicationTheme.Dark,
    };

    private static void ApplyCurrent()
    {
        var theme = Resolve(_mode, SystemPrefersLight());
        if (ApplicationThemeManager.GetAppTheme() != theme || !_appliedOnce)
        {
            ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica, updateAccent: false);
            // 앱 창(편집·환경설정)만. 캡처 오버레이는 정지 화면을 그리는 창이라 배경을 바꾸면 안 된다.
            foreach (var window in Application.Current.Windows.OfType<FluentWindow>())
                WindowBackgroundManager.UpdateBackground(window, theme, WindowBackdropType.Mica);
        }
        _appliedOnce = true;
        ApplyAppColors(theme);
    }

    private static bool _appliedOnce;

    private static void ApplyAppColors(ApplicationTheme theme)
    {
        ApplicationAccentColorManager.Apply(Accent, theme);
        var dark = theme != ApplicationTheme.Light;
        // 앱 최상위 리소스는 테마 사전보다 우선하므로, 토글 버튼 템플릿이 찾는 키를 여기서 덮어쓴다.
        var resources = Application.Current.Resources;
        var text = dark ? Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xE4, 0x00, 0x00, 0x00);
        resources["ToggleButtonBackgroundChecked"] = Brush(dark ? 0x38FFFFFFu : 0x1F000000u);
        resources["ToggleButtonForegroundCheckedPointerOver"] = Brush(dark ? 0x44FFFFFFu : 0x29000000u); // 템플릿이 선택+마우스 올림 배경으로 이 키를 씀
        resources["ToggleButtonBackgroundCheckedPressed"] = Brush(dark ? 0x2CFFFFFFu : 0x17000000u);
        resources["ToggleButtonForegroundChecked"] = Freeze(new SolidColorBrush(text));
        resources["ToggleButtonForegroundCheckedPressed"] = Freeze(new SolidColorBrush(text));
        resources["ToggleButtonBorderBrushCheckedPressed"] = Brush(dark ? 0x12FFFFFFu : 0x0F000000u);
        // 선택된 토글의 테두리도 강조색 대신 보통 버튼 테두리로
        if (Application.Current.TryFindResource("ControlElevationBorderBrush") is Brush border)
            resources["AccentControlElevationBorderBrush"] = border;
    }

    private static SolidColorBrush Brush(uint argb) =>
        Freeze(new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb)));

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    private static bool SystemPrefersLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
    }
}
