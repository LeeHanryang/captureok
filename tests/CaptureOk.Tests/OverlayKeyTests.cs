using CaptureOk.Capture;

namespace CaptureOk.Tests;

public class OverlayKeyTests
{
    private const int WM_SYSCOMMAND = 0x0112;

    [Theory(DisplayName = "keyboard-menu-activation-is-swallowed")]
    [InlineData(WM_SYSCOMMAND, 0xF100, true)]  // SC_KEYMENU (Alt·F10 단독)
    [InlineData(WM_SYSCOMMAND, 0xF102, true)]  // 하위 4비트는 시스템이 쓰는 값
    [InlineData(WM_SYSCOMMAND, 0xF060, false)] // SC_CLOSE (Alt+F4)는 그대로
    [InlineData(0x0100, 0xF100, false)]        // 다른 메시지
    public void KeyboardMenuActivationIsSwallowed(int msg, int wParam, bool expected) =>
        Assert.Equal(expected, OverlayWindow.IsKeyboardMenuActivation(msg, (IntPtr)wParam));
}
