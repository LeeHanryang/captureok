namespace CaptureOk.Core.Geometry;

/// <summary>이미지 픽셀 좌표의 점 (소수 허용).</summary>
public readonly record struct PointD(double X, double Y);

/// <summary>이미지 픽셀 좌표의 사각형 (소수 허용).</summary>
public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public static RectD FromPoints(PointD a, PointD b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));

    public bool Contains(PointD p) => p.X >= X && p.X <= Right && p.Y >= Y && p.Y <= Bottom;

    public RectD Inflate(double d) => new(X - d, Y - d, Width + d * 2, Height + d * 2);

    /// <summary>(0,0)-(width,height) 안으로 자른다. 겹치지 않으면 크기 0.</summary>
    public RectD ClampTo(double width, double height)
    {
        var left = Math.Clamp(X, 0, width);
        var top = Math.Clamp(Y, 0, height);
        return new RectD(left, top, Math.Max(0, Math.Clamp(Right, 0, width) - left), Math.Max(0, Math.Clamp(Bottom, 0, height) - top));
    }

    public RectD Union(RectD other)
    {
        var left = Math.Min(X, other.X);
        var top = Math.Min(Y, other.Y);
        return new RectD(left, top, Math.Max(Right, other.Right) - left, Math.Max(Bottom, other.Bottom) - top);
    }
}
