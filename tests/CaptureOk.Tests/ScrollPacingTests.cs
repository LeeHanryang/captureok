using CaptureOk.Core.Geometry;
using CaptureOk.Core.Imaging;

namespace CaptureOk.Tests;

public class ScrollPacingTests
{
    private static readonly PixelRect Monitor = new(0, 0, 1920, 1080);

    [Theory(DisplayName = "next-delta-aims-for-half-the-band")]
    [InlineData(100, 120, 800, 480)] // 한 칸에 100줄 → 400줄 움직이려면 네 칸
    [InlineData(400, 120, 800, 120)] // 이미 절반 = 그대로 한 칸
    [InlineData(300, 120, 300, 40)]  // 낮은 영역: 한 칸이 너무 큼 → 1/3칸
    [InlineData(5, 120, 2000, 1200)] // 아주 조금씩 움직이는 프로그램: 최대 10칸
    public void NextDeltaAimsForHalfTheBand(int lastShift, int delta, int band, int expected) =>
        Assert.Equal(expected, ScrollPacing.NextDelta(lastShift, delta, band));

    [Fact(DisplayName = "next-delta-is-whole-notches-above-one-notch")]
    public void NextDeltaIsWholeNotchesAboveOneNotch() =>
        Assert.Equal(0, ScrollPacing.NextDelta(70, 120, 800) % 120); // 칸 단위로만 움직이는 프로그램이 많다

    [Theory(DisplayName = "backoff-halves-down-to-a-third-notch")]
    [InlineData(480, 240)]
    [InlineData(120, 40)]
    [InlineData(80, 40)]
    [InlineData(40, 40)]
    public void BackoffHalvesDownToAThirdNotch(int delta, int expected) =>
        Assert.Equal(expected, ScrollPacing.Backoff(delta));

    [Fact(DisplayName = "park-point-is-outside-the-region-on-the-right")]
    public void ParkPointIsOutsideTheRegionOnTheRight()
    {
        var region = new PixelRect(100, 200, 800, 600);
        var (x, y) = ScrollPacing.ParkPoint(region, Monitor);
        Assert.False(region.Contains(x, y));
        Assert.True(x >= region.Right && x < Monitor.Right);
    }

    [Fact(DisplayName = "park-point-uses-left-when-region-touches-right-edge")]
    public void ParkPointUsesLeftWhenRegionTouchesRightEdge()
    {
        var region = new PixelRect(1000, 200, 920, 600);
        var (x, _) = ScrollPacing.ParkPoint(region, Monitor);
        Assert.True(x < region.X && x >= 0);
    }

    [Fact(DisplayName = "park-point-for-full-width-region-is-above")]
    public void ParkPointForFullWidthRegionIsAbove()
    {
        var region = new PixelRect(0, 100, 1920, 900);
        var (x, y) = ScrollPacing.ParkPoint(region, Monitor);
        Assert.False(region.Contains(x, y));
        Assert.True(y < region.Y);
    }

    [Fact(DisplayName = "indicator-avoids-the-region")]
    public void IndicatorAvoidsTheRegion()
    {
        var topRegion = new PixelRect(500, 0, 800, 400); // 화면 위쪽 영역 → 표시는 아래로
        var place = ScrollPacing.IndicatorPlace(topRegion, Monitor, 400, 40, 1.0, hiddenFromCapture: false);
        Assert.True(place.Visible);
        Assert.True(new PixelRect(place.X, place.Y, 400, 40).Intersect(topRegion).IsEmpty);
        Assert.True(place.Y > 400);
    }

    [Fact(DisplayName = "indicator-on-full-screen-region-shows-only-if-hidden-from-capture")]
    public void IndicatorOnFullScreenRegionShowsOnlyIfHiddenFromCapture()
    {
        Assert.True(ScrollPacing.IndicatorPlace(Monitor, Monitor, 400, 40, 1.0, hiddenFromCapture: true).Visible);
        Assert.False(ScrollPacing.IndicatorPlace(Monitor, Monitor, 400, 40, 1.0, hiddenFromCapture: false).Visible);
    }
}
