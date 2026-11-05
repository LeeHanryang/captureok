using CaptureOk.Core.Hotkeys;

namespace CaptureOk.Tests;

public class HotkeyGestureTests
{
    [Fact(DisplayName = "parses-ctrl-shift-letter")]
    public void ParsesCtrlShiftLetter()
    {
        Assert.True(HotkeyGesture.TryParse("Ctrl+Shift+C", out var g));
        Assert.Equal(new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x43), g);
    }

    [Fact(DisplayName = "parses-printscreen")]
    public void ParsesPrintScreen()
    {
        Assert.True(HotkeyGesture.TryParse("PrintScreen", out var g));
        Assert.Equal(new HotkeyGesture(HotkeyModifiers.None, 0x2C), g);
    }

    [Fact(DisplayName = "parses-case-and-spaces")]
    public void ParsesCaseAndSpaces()
    {
        Assert.True(HotkeyGesture.TryParse(" ctrl + shift + w ", out var g));
        Assert.Equal(new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x57), g);
    }

    [Fact(DisplayName = "parses-function-key")]
    public void ParsesFunctionKey()
    {
        Assert.True(HotkeyGesture.TryParse("Alt+F4", out var g));
        Assert.Equal(new HotkeyGesture(HotkeyModifiers.Alt, 0x73), g);
    }

    [Fact(DisplayName = "formats-canonical-order")]
    public void FormatsCanonicalOrder()
    {
        var g = new HotkeyGesture(HotkeyModifiers.Shift | HotkeyModifiers.Control | HotkeyModifiers.Win, 0x41);
        Assert.Equal("Ctrl+Shift+Win+A", g.ToString());
    }

    [Theory(DisplayName = "rejects-invalid")]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Foo+C")]
    [InlineData("Ctrl+C+D")]
    public void RejectsInvalid(string text)
    {
        Assert.False(HotkeyGesture.TryParse(text, out _));
    }
}
