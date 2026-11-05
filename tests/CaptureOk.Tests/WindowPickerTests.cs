using CaptureOk.Core.Geometry;
using CaptureOk.Core.Windows;

namespace CaptureOk.Tests;

public class WindowPickerTests
{
    [Fact(DisplayName = "hit-test-topmost-first")]
    public void HitTestTopmostFirst()
    {
        var front = new WindowInfo(1, new PixelRect(100, 100, 400, 300), "front");
        var back = new WindowInfo(2, new PixelRect(0, 0, 1000, 800), "back");
        var zOrder = new[] { front, back };

        Assert.Same(front, WindowPicker.HitTest(zOrder, 150, 150));
        Assert.Same(back, WindowPicker.HitTest(zOrder, 900, 700));
        Assert.Null(WindowPicker.HitTest(zOrder, 1500, 50));
    }
}
