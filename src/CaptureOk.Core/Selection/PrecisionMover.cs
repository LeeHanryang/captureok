namespace CaptureOk.Core.Selection;

/// <summary>
/// 드래그 중 보조 키를 누르면 마우스 이동량을 1/16로 줄인다 (마우스 16px = 1px).
/// 반환값은 커서를 옮겨야 할 위치이며, 호출자는 그 위치로 실제 커서를 옮긴다고 가정한다.
/// </summary>
public sealed class PrecisionMover
{
    private const double SlowFactor = 1.0 / 16;

    private int _x, _y;
    private double _fracX, _fracY;

    public void Reset(int x, int y)
    {
        (_x, _y) = (x, y);
        (_fracX, _fracY) = (0, 0);
    }

    public (int X, int Y) Move(int rawX, int rawY, bool slow)
    {
        if (!slow)
        {
            Reset(rawX, rawY);
            return (_x, _y);
        }

        _fracX += (rawX - _x) * SlowFactor;
        _fracY += (rawY - _y) * SlowFactor;
        var stepX = (int)Math.Truncate(_fracX);
        var stepY = (int)Math.Truncate(_fracY);
        _fracX -= stepX;
        _fracY -= stepY;
        _x += stepX;
        _y += stepY;
        return (_x, _y);
    }
}
