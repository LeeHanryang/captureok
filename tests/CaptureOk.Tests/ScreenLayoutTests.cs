using CaptureOk.Core.Geometry;

namespace CaptureOk.Tests;

public class ScreenLayoutTests
{
    internal static ScreenLayout TwoSideBySide() => new([
        new MonitorInfo(new PixelRect(0, 0, 2560, 1440), 1.0, IsPrimary: true),
        new MonitorInfo(new PixelRect(2560, 0, 2560, 1440), 1.0, IsPrimary: false),
    ]);

    [Fact(DisplayName = "virtual-bounds-two-side-by-side")]
    public void VirtualBoundsTwoSideBySide()
    {
        Assert.Equal(new PixelRect(0, 0, 5120, 1440), TwoSideBySide().VirtualBounds);
    }

    [Fact(DisplayName = "negative-origin")]
    public void NegativeOrigin()
    {
        var secondary = new MonitorInfo(new PixelRect(-2560, -200, 2560, 1440), 1.0, IsPrimary: false);
        var layout = new ScreenLayout([new MonitorInfo(new PixelRect(0, 0, 1920, 1080), 1.0, IsPrimary: true), secondary]);

        Assert.Equal(new PixelRect(-2560, -200, 4480, 1440), layout.VirtualBounds);
        Assert.Same(secondary, layout.MonitorAt(-10, 0));
    }

    [Fact(DisplayName = "to-local-dip-scaled")]
    public void ToLocalDipScaled()
    {
        var m = new MonitorInfo(new PixelRect(2560, 0, 3840, 2160), 1.5, IsPrimary: false);
        var layout = new ScreenLayout([m]);

        Assert.Equal((100.0, 100.0), layout.ToLocalDip(m, 2710, 150));
    }

    [Fact(DisplayName = "from-points-any-direction")]
    public void FromPointsAnyDirection()
    {
        Assert.Equal(new PixelRect(5, 3, 6, 8), PixelRect.FromPoints(10, 10, 5, 3));
    }

    [Fact(DisplayName = "inflate-grows-every-side")]
    public void InflateGrowsEverySide()
    {
        Assert.Equal(new PixelRect(-3, 7, 106, 56), new PixelRect(0, 10, 100, 50).Inflate(3));
    }

    [Fact(DisplayName = "clamp-to-virtual")]
    public void ClampToVirtual()
    {
        Assert.Equal(new PixelRect(5000, 1400, 120, 40), TwoSideBySide().Clamp(new PixelRect(5000, 1400, 500, 500)));
    }
}
