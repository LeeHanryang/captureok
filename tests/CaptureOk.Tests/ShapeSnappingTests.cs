using CaptureOk.Core.Editing;
using CaptureOk.Core.Geometry;

namespace CaptureOk.Tests;

public class ShapeSnappingTests
{
    private static PointD Polar(double degrees, double length) =>
        new(Math.Cos(degrees * Math.PI / 180) * length, Math.Sin(degrees * Math.PI / 180) * length);

    [Theory(DisplayName = "snap-axis-makes-perfect-horizontal-or-vertical")]
    [InlineData(10, 20, 110, 27, 110, 20)]   // 조금 기울어도 완전한 가로
    [InlineData(10, 20, -80, 25, -80, 20)]   // 왼쪽으로도
    [InlineData(10, 20, 13, 120, 10, 120)]   // 완전한 세로
    [InlineData(10, 20, 8, -70, 10, -70)]    // 위로도
    [InlineData(0, 0, 50, 50, 50, 0)]        // 정확히 대각선이면 가로
    [InlineData(5, 5, 5, 5, 5, 5)]           // 안 움직임
    public void SnapAxisMakesPerfectHorizontalOrVertical(double sx, double sy, double ex, double ey, double rx, double ry) =>
        Assert.Equal(new PointD(rx, ry), ShapeSnapping.SnapAxis(new PointD(sx, sy), new PointD(ex, ey)));

    [Fact(DisplayName = "stroke-modifiers")]
    public void StrokeModifiers()
    {
        var start = new PointD(0, 0);
        var p = new PointD(100, 9);
        Assert.Null(ShapeSnapping.StraightStrokeEnd(start, p, shift: false, ctrl: false));                  // 자유 곡선
        Assert.Equal(new PointD(100, 0), ShapeSnapping.StraightStrokeEnd(start, p, shift: true, ctrl: false)); // Shift = 가로·세로
        Assert.Equal(p, ShapeSnapping.StraightStrokeEnd(start, p, shift: false, ctrl: true));                  // Ctrl = 원하는 각도의 직선
        Assert.Equal(new PointD(100, 0), ShapeSnapping.StraightStrokeEnd(start, p, shift: true, ctrl: true));  // 둘 다면 Shift
    }

    [Fact(DisplayName = "snap45-horizontal")]
    public void Snap45Horizontal()
    {
        var end = ShapeSnapping.Snap45(new PointD(0, 0), Polar(10, 100));
        Assert.Equal(100, end.X, 3);
        Assert.Equal(0, end.Y, 3);
    }

    [Fact(DisplayName = "snap45-diagonal")]
    public void Snap45Diagonal()
    {
        var end = ShapeSnapping.Snap45(new PointD(0, 0), Polar(40, 100));
        var expected = Polar(45, 100);
        Assert.Equal(expected.X, end.X, 3);
        Assert.Equal(expected.Y, end.Y, 3);
    }

    [Fact(DisplayName = "snap-square-keeps-direction")]
    public void SnapSquareKeepsDirection()
    {
        // 왼쪽 위로 드래그: 짧은 변(20)에 맞춘 정사각형
        Assert.Equal(new PointD(80, 80), ShapeSnapping.SnapSquare(new PointD(100, 100), new PointD(40, 80)));
    }

    [Fact(DisplayName = "next-number-empty-is-1")]
    public void NextNumberEmptyIs1()
    {
        Assert.Equal(1, ShapeSnapping.NextNumber([]));
    }

    [Fact(DisplayName = "next-number-is-max-plus-1")]
    public void NextNumberIsMaxPlus1()
    {
        Annotation[] items =
        [
            new NumberAnnotation(Guid.NewGuid(), new PointD(0, 0), 1, 0xFFFF0000, 28),
            new ShapeAnnotation(Guid.NewGuid(), ShapeKind.Rectangle, new RectD(0, 0, 1, 1), new StrokeStyle(0xFF000000, 1, false), false),
            new NumberAnnotation(Guid.NewGuid(), new PointD(0, 0), 3, 0xFFFF0000, 28),
        ];
        Assert.Equal(4, ShapeSnapping.NextNumber(items));
    }

    [Fact(DisplayName = "palette-has-8-colors-red-first")]
    public void PaletteHas8ColorsRedFirst()
    {
        Assert.Equal(8, DrawingOptions.Palette.Count);
        Assert.Equal(0xFFFF0000u, DrawingOptions.Palette[0].Argb);
        Assert.Equal([1, 2, 3, 5, 8], DrawingOptions.Widths);
    }
}
