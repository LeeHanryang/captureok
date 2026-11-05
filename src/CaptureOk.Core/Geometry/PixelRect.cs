namespace CaptureOk.Core.Geometry;

/// <summary>물리 픽셀 사각형. 좌표는 가상 화면 기준이며 음수일 수 있다. Right·Bottom은 끝(제외) 좌표.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;

    /// <summary>겹치는 부분. 겹치지 않으면 Width/Height가 0인 사각형.</summary>
    public PixelRect Intersect(PixelRect other)
    {
        var left = Math.Max(X, other.X);
        var top = Math.Max(Y, other.Y);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);
        return right <= left || bottom <= top
            ? new PixelRect(left, top, 0, 0)
            : new PixelRect(left, top, right - left, bottom - top);
    }

    /// <summary>두 점(둘 다 포함)을 잇는 사각형. 방향은 상관없다.</summary>
    public static PixelRect FromPoints(int x1, int y1, int x2, int y2) =>
        new(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1) + 1, Math.Abs(y2 - y1) + 1);
}

public readonly record struct PixelSize(int Width, int Height);
