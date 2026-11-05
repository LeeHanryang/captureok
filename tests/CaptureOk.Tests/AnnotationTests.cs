using CaptureOk.Core.Editing;
using CaptureOk.Core.Geometry;

namespace CaptureOk.Tests;

public class AnnotationTests
{
    private static readonly StrokeStyle Red3 = new(0xFFFF0000, 3, Dashed: false);

    [Fact(DisplayName = "rect-from-points-normalizes")]
    public void RectFromPointsNormalizes()
    {
        Assert.Equal(new RectD(2, 3, 8, 7), RectD.FromPoints(new PointD(10, 10), new PointD(2, 3)));
    }

    [Fact(DisplayName = "stroke-bounds-include-half-width")]
    public void StrokeBoundsIncludeHalfWidth()
    {
        var s = new StrokeAnnotation(Guid.NewGuid(), [new PointD(10, 10), new PointD(20, 30)], Red3, Highlighter: false);
        Assert.Equal(new RectD(8.5, 8.5, 13, 23), s.Bounds);
    }

    [Fact(DisplayName = "highlighter-bounds-use-drawn-width")]
    public void HighlighterBoundsUseDrawnWidth()
    {
        var s = new StrokeAnnotation(Guid.NewGuid(), [new PointD(10, 10), new PointD(20, 10)], Red3, Highlighter: true);
        // 그리는 굵기 = 3 × 4 = 12 → 반 = 6
        Assert.Equal(new RectD(4, 4, 22, 12), s.Bounds);
    }

    [Fact(DisplayName = "shape-offset-moves-rect")]
    public void ShapeOffsetMovesRect()
    {
        var s = new ShapeAnnotation(Guid.NewGuid(), ShapeKind.Rectangle, new RectD(1, 2, 3, 4), Red3, Filled: false);
        Assert.Equal(new RectD(11, 22, 3, 4), ((ShapeAnnotation)s.Offset(10, 20)).Rect);
    }

    [Fact(DisplayName = "line-offset-moves-both-ends")]
    public void LineOffsetMovesBothEnds()
    {
        var l = new LineAnnotation(Guid.NewGuid(), new PointD(0, 0), new PointD(5, 5), Red3, Arrow: true);
        var moved = (LineAnnotation)l.Offset(1, 2);
        Assert.Equal(new PointD(1, 2), moved.Start);
        Assert.Equal(new PointD(6, 7), moved.End);
    }

    [Fact(DisplayName = "text-bounds")]
    public void TextBounds()
    {
        var t = new TextAnnotation(Guid.NewGuid(), new PointD(5, 6), "안녕", 0xFF000000, 20, Width: 40, Height: 27);
        Assert.Equal(new RectD(5, 6, 40, 27), t.Bounds);
    }

    [Fact(DisplayName = "number-bounds")]
    public void NumberBounds()
    {
        var n = new NumberAnnotation(Guid.NewGuid(), new PointD(50, 50), 1, 0xFFFF0000, 28);
        Assert.Equal(new RectD(36, 36, 28, 28), n.Bounds);
    }

    [Fact(DisplayName = "offset-keeps-id-and-style")]
    public void OffsetKeepsIdAndStyle()
    {
        var id = Guid.NewGuid();
        var s = new StrokeAnnotation(id, [new PointD(0, 0)], Red3, Highlighter: false);
        var moved = (StrokeAnnotation)s.Offset(3, 4);
        Assert.Equal(id, moved.Id);
        Assert.Equal(Red3, moved.Style);
        Assert.Equal([new PointD(3, 4)], moved.Points);
    }
}
