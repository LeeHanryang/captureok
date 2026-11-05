using CaptureOk.Core.Settings;
using Wpf.Ui.Appearance;

namespace CaptureOk.Tests;

public class AppThemeTests
{
    [Theory(DisplayName = "fixed-theme-ignores-windows-mode")]
    [InlineData(ThemeMode.Light, false, ApplicationTheme.Light)]
    [InlineData(ThemeMode.Dark, true, ApplicationTheme.Dark)]
    [InlineData(ThemeMode.System, true, ApplicationTheme.Light)]
    [InlineData(ThemeMode.System, false, ApplicationTheme.Dark)]
    public void FixedThemeIgnoresWindowsMode(ThemeMode mode, bool windowsLight, ApplicationTheme expected) =>
        Assert.Equal(expected, AppTheme.Resolve(mode, windowsLight));
}
