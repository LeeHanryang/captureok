using CaptureOk.Core.Geometry;

namespace CaptureOk.Core.Editing;

/// <summary>Shift 보정과 번호 계산.</summary>
public static class ShapeSnapping
{
    /// <summary>시작점 기준 45도 단위로 방향을 맞춘 끝점 (길이 유지). 직선·화살표용.</summary>
    public static PointD Snap45(PointD start, PointD end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length == 0) return end;
        var step = Math.PI / 4;
        var angle = Math.Round(Math.Atan2(dy, dx) / step) * step;
        return new PointD(start.X + Math.Cos(angle) * length, start.Y + Math.Sin(angle) * length);
    }

    /// <summary>시작점에서 더 많이 움직인 방향으로 맞춘 완전한 가로선·세로선의 끝점 (같으면 가로).</summary>
    public static PointD SnapAxis(PointD start, PointD end) =>
        Math.Abs(end.X - start.X) >= Math.Abs(end.Y - start.Y) ? new PointD(end.X, start.Y) : new PointD(start.X, end.Y);

    /// <summary>펜·형광펜을 직선으로 그릴 때의 끝점: Shift = 가로·세로, Ctrl = 원하는 각도(둘 다면 Shift). 자유 곡선이면 null.</summary>
    public static PointD? StraightStrokeEnd(PointD start, PointD end, bool shift, bool ctrl) =>
        shift ? SnapAxis(start, end) : ctrl ? end : null;

    /// <summary>시작점 기준, 드래그 방향을 유지한 채 짧은 변에 맞춘 정사각형의 끝점. 사각형·원용.</summary>
    public static PointD SnapSquare(PointD start, PointD end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var side = Math.Min(Math.Abs(dx), Math.Abs(dy));
        return new PointD(start.X + Math.Sign(dx) * side, start.Y + Math.Sign(dy) * side);
    }

    /// <summary>문서에 있는 번호 중 가장 큰 값 + 1 (없으면 1).</summary>
    public static int NextNumber(IReadOnlyList<Annotation> annotations) =>
        annotations.OfType<NumberAnnotation>().Select(n => n.Number).DefaultIfEmpty(0).Max() + 1;
}
