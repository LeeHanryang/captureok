using CaptureOk.Core.Geometry;

namespace CaptureOk.Core.Editing;

/// <summary>
/// 펜·형광펜으로 그리는 한 획의 점 목록. 자유 곡선으로 시작하고, Shift를 누르면 가로·세로 직선, Ctrl을 누르면 원하는 각도의 직선이 된다.
/// 한번 직선이 되면 단추를 뗄 때까지 직선으로 남는다 — 단추보다 키를 먼저 떼도 끝에 갈고리가 생기지 않게.
/// </summary>
public sealed class StrokeBuilder(PointD start, double minSpacing)
{
    private enum Kind { Free, Axis, Angle }

    private Kind _kind;
    private List<PointD> _points = [start];

    public IReadOnlyList<PointD> Points => _points;

    /// <returns>점 목록이 바뀌었으면 true (자유 곡선에서 앞 점과 너무 가까우면 false)</returns>
    public bool Add(PointD p, bool shift, bool ctrl)
    {
        if (shift) _kind = Kind.Axis;
        else if (ctrl) _kind = Kind.Angle;

        if (_kind != Kind.Free)
        {
            _points = [start, ShapeSnapping.StraightStrokeEnd(start, p, _kind == Kind.Axis, _kind == Kind.Angle)!.Value];
            return true;
        }

        var last = _points[^1];
        if (Math.Abs(p.X - last.X) < minSpacing && Math.Abs(p.Y - last.Y) < minSpacing) return false;
        _points.Add(p);
        return true;
    }
}
