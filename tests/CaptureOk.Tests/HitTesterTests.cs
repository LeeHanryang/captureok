using CaptureOk.Core.Editing;
using CaptureOk.Core.Geometry;

namespace CaptureOk.Tests;

public class HitTesterTests
{
    private const double Tolerance = 4;
    private static readonly StrokeStyle Red3 = new(0xFFFF0000, 3, Dashed: false);

    private static Annotation? Hit(Annotation a, double x, double y) => HitTester.Hit([a], new PointD(x, y), Tolerance);

    [Fact(DisplayName = "stroke-near-segment-hits")]
    public void StrokeNearSegmentHits()
    {
        var s = new StrokeAnnotation(Guid.NewGuid(), [new PointD(0, 0), new PointD(100, 0)], Red3, false);
        Assert.Same(s, Hit(s, 50, 5)); // 1.5 + 4 = 5.5 이내
    }

    [Fact(DisplayName = "stroke-far-misses")]
    public void StrokeFarMisses()
    {
        var s = new StrokeAnnotation(Guid.NewGuid(), [new PointD(0, 0), new PointD(100, 0)], Red3, false);
        Assert.Null(Hit(s, 50, 7));
    }

    [Fact(DisplayName = "unfilled-rect-center-misses")]
    public void UnfilledRectCenterMisses()
    {
        var r = new ShapeAnnotation(Guid.NewGuid(), ShapeKind.Rectangle, new RectD(0, 0, 100, 100), Red3, Filled: false);
        Assert.Null(Hit(r, 50, 50));
    }

    [Fact(DisplayName = "unfilled-rect-edge-hits")]
    public void UnfilledRectEdgeHits()
    {
        var r = new ShapeAnnotation(Guid.NewGuid(), ShapeKind.Rectangle, new RectD(0, 0, 100, 100), Red3, Filled: false);
        Assert.Same(r, Hit(r, 98, 50));
    }

    [Fact(DisplayName = "filled-rect-center-hits")]
    public void FilledRectCenterHits()
    {
        var r = new ShapeAnnotation(Guid.NewGuid(), ShapeKind.Rectangle, new RectD(0, 0, 100, 100), Red3, Filled: true);
        Assert.Same(r, Hit(r, 50, 50));
    }

    [Fact(DisplayName = "ellipse-edge-hits")]
    public void EllipseEdgeHits()
    {
        var e = new ShapeAnnotation(Guid.NewGuid(), ShapeKind.Ellipse, new RectD(0, 0, 200, 100), Red3, Filled: false);
        Assert.Same(e, Hit(e, 100, 2));   // 위쪽 가장자리
        Assert.Null(Hit(e, 100, 50));     // 가운데
        Assert.Null(Hit(e, 2, 2));        // 사각형 모서리(원 바깥)
    }

    [Fact(DisplayName = "flat-ellipse-ends-are-not-too-lenient")]
    public void FlatEllipseEndsAreNotTooLenient()
    {
        // "이 문장에 동그라미" 같은 납작한 원: 300×30
        var e = new ShapeAnnotation(Guid.NewGuid(), ShapeKind.Ellipse, new RectD(0, 0, 300, 30), Red3, Filled: false);
        Assert.Same(e, Hit(e, 3, 15));     // 왼쪽 끝 바로 안
        Assert.Same(e, Hit(e, 150, 1));    // 위쪽 가장자리
        Assert.Null(Hit(e, -20, 15));      // 왼쪽 끝에서 20px 바깥
        Assert.Null(Hit(e, 150, 15));      // 가운데
    }

    [Fact(DisplayName = "arrow-head-hits")]
    public void ArrowHeadHits()
    {
        var a = new LineAnnotation(Guid.NewGuid(), new PointD(0, 50), new PointD(100, 50), Red3, Arrow: true);
        // 머리 길이 12, 너비 9.6 → 끝 바로 앞, 선에서 4.5 떨어진 점은 머리 안
        Assert.Same(a, Hit(a, 95, 54.5));
    }

    [Fact(DisplayName = "text-inside-hits")]
    public void TextInsideHits()
    {
        var t = new TextAnnotation(Guid.NewGuid(), new PointD(10, 10), "가", 0xFF000000, 20, 30, 25);
        Assert.Same(t, Hit(t, 20, 20));
        Assert.Null(Hit(t, 60, 60));
    }

    [Fact(DisplayName = "number-inside-hits")]
    public void NumberInsideHits()
    {
        var n = new NumberAnnotation(Guid.NewGuid(), new PointD(50, 50), 1, 0xFFFF0000, 28);
        Assert.Same(n, Hit(n, 55, 55));
        Assert.Null(Hit(n, 70, 70));
    }

    [Fact(DisplayName = "topmost-wins")]
    public void TopmostWins()
    {
        var below = new ShapeAnnotation(Guid.NewGuid(), ShapeKind.Rectangle, new RectD(0, 0, 100, 100), Red3, Filled: true);
        var above = new ShapeAnnotation(Guid.NewGuid(), ShapeKind.Rectangle, new RectD(40, 40, 20, 20), Red3, Filled: true);
        Assert.Same(above, HitTester.Hit([below, above], new PointD(50, 50), Tolerance));
    }
}
