using CaptureOk.Core.Geometry;

namespace CaptureOk.Core.Editing;

/// <summary>점(이미지 픽셀)에 있는 객체를 찾는다. 나중에 그린(위에 있는) 객체가 우선.</summary>
public static class HitTester
{
    /// <summary>
    /// 지우개 드래그: from→to 선분을 따라가며 닿는 객체를 지나간 순서대로 돌려준다 (exclude에 있는 것은 건너뜀).
    /// 마우스가 빠르게 움직여도 사이의 객체를 놓치지 않도록 허용 오차 간격으로 표본을 잡는다.
    /// </summary>
    public static List<Annotation> HitAlong(IReadOnlyList<Annotation> annotations, PointD from, PointD to, double tolerance, IReadOnlySet<Guid> exclude,
        Func<Annotation, bool>? canHit = null)
    {
        var hits = new List<Annotation>();
        var skip = new HashSet<Guid>(exclude);
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        // 한 점(클릭)은 그 점 하나만, 이어지는 선분은 이전에 본 시작점을 빼고 끝점까지 본다.
        var steps = length == 0 ? 0 : Math.Max(1, (int)Math.Ceiling(length / Math.Max(0.5, tolerance)));
        for (var i = steps == 0 ? 0 : 1; i <= steps; i++)
        {
            var p = steps == 0 ? from : new PointD(from.X + dx * i / steps, from.Y + dy * i / steps);
            for (var j = annotations.Count - 1; j >= 0; j--)
            {
                var a = annotations[j];
                if (skip.Contains(a.Id) || (canHit is not null && !canHit(a)) || !IsHit(a, p, tolerance)) continue;
                hits.Add(a);
                skip.Add(a.Id);
                break; // 한 지점에서는 맨 위 하나만 (겹친 아래 객체는 다시 지나갈 때)
            }
        }
        return hits;
    }

    public static Annotation? Hit(IReadOnlyList<Annotation> annotations, PointD p, double tolerance)
    {
        for (var i = annotations.Count - 1; i >= 0; i--)
        {
            if (IsHit(annotations[i], p, tolerance)) return annotations[i];
        }
        return null;
    }

    private static bool IsHit(Annotation a, PointD p, double tol) => a switch
    {
        StrokeAnnotation s => DistanceToPolyline(s.Points, p) <= s.DrawnWidth / 2 + tol,
        ShapeAnnotation { Kind: ShapeKind.Rectangle } r => r.Filled
            ? r.Rect.Inflate(tol).Contains(p)
            : DistanceToRectBorder(r.Rect, p) <= r.Style.Width / 2 + tol,
        ShapeAnnotation e => EllipseHit(e, p, tol),
        LineAnnotation l => DistanceToSegment(l.Start, l.End, p) <= l.Style.Width / 2 + tol || (l.Arrow && InArrowHead(l, p)),
        TextAnnotation t => t.Bounds.Inflate(tol).Contains(p),
        NumberAnnotation n => Distance(n.Center, p) <= n.Diameter / 2 + tol,
        MosaicAnnotation or BlurAnnotation => a.Bounds.Inflate(tol).Contains(p),
        _ => a.Bounds.Inflate(tol).Contains(p),
    };

    private static bool EllipseHit(ShapeAnnotation e, PointD p, double tol)
    {
        var rx = e.Rect.Width / 2;
        var ry = e.Rect.Height / 2;
        if (rx <= 0 || ry <= 0) return false;
        var dx = p.X - (e.Rect.X + rx);
        var dy = p.Y - (e.Rect.Y + ry);
        // F = (dx/rx)² + (dy/ry)² - 1 (테두리에서 0). 테두리까지 거리 ≈ |F| / |∇F|.
        // 반지름 비율로만 나누면 납작한 원의 양 끝에서 판정 범위가 rx/ry배로 넓어진다.
        var f = dx * dx / (rx * rx) + dy * dy / (ry * ry) - 1;
        if (e.Filled && f <= 0) return true;
        var gx = 2 * dx / (rx * rx);
        var gy = 2 * dy / (ry * ry);
        var gradient = Math.Sqrt(gx * gx + gy * gy);
        if (gradient == 0) return false; // 정확히 중심 (채우지 않은 원)
        var distance = Math.Abs(f) / gradient;
        return distance <= (e.Filled ? tol : e.Style.Width / 2 + tol);
    }

    private static bool InArrowHead(LineAnnotation l, PointD p)
    {
        var dx = l.End.X - l.Start.X;
        var dy = l.End.Y - l.Start.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len == 0) return false;
        var (ux, uy) = (dx / len, dy / len);
        var baseCenter = new PointD(l.End.X - ux * l.HeadLength, l.End.Y - uy * l.HeadLength);
        var half = l.HeadWidth / 2;
        var left = new PointD(baseCenter.X - uy * half, baseCenter.Y + ux * half);
        var right = new PointD(baseCenter.X + uy * half, baseCenter.Y - ux * half);
        return InTriangle(p, l.End, left, right);
    }

    private static bool InTriangle(PointD p, PointD a, PointD b, PointD c)
    {
        static double Cross(PointD o, PointD u, PointD v) => (u.X - o.X) * (v.Y - o.Y) - (u.Y - o.Y) * (v.X - o.X);
        var d1 = Cross(p, a, b);
        var d2 = Cross(p, b, c);
        var d3 = Cross(p, c, a);
        var hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
        var hasPos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(hasNeg && hasPos);
    }

    private static double DistanceToRectBorder(RectD r, PointD p)
    {
        PointD tl = new(r.X, r.Y), tr = new(r.Right, r.Y), br = new(r.Right, r.Bottom), bl = new(r.X, r.Bottom);
        return Math.Min(Math.Min(DistanceToSegment(tl, tr, p), DistanceToSegment(tr, br, p)),
                        Math.Min(DistanceToSegment(br, bl, p), DistanceToSegment(bl, tl, p)));
    }

    private static double DistanceToPolyline(IReadOnlyList<PointD> points, PointD p)
    {
        if (points.Count == 0) return double.MaxValue;
        if (points.Count == 1) return Distance(points[0], p);
        var best = double.MaxValue;
        for (var i = 1; i < points.Count; i++) best = Math.Min(best, DistanceToSegment(points[i - 1], points[i], p));
        return best;
    }

    private static double DistanceToSegment(PointD a, PointD b, PointD p)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lengthSq = dx * dx + dy * dy;
        if (lengthSq == 0) return Distance(a, p);
        var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSq, 0, 1);
        return Distance(new PointD(a.X + t * dx, a.Y + t * dy), p);
    }

    private static double Distance(PointD a, PointD b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
