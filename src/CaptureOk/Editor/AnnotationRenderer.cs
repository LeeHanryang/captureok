using System.Globalization;
using System.Windows;
using System.Windows.Media;
using CaptureOk.Core.Editing;
using CaptureOk.Core.Geometry;

namespace CaptureOk.Editor;

/// <summary>
/// 편집 객체를 이미지 픽셀 좌표 그대로 그린다. 화면(캔버스)과 내보내기(Flattener)가 같이 써서 결과가 어긋나지 않게 한다.
/// 화면에서는 호출자가 확대·DPI 변환을 DrawingContext에 먼저 적용한다.
/// </summary>
internal static class AnnotationRenderer
{
    private static readonly Typeface TextFace = new("Malgun Gothic");
    private static readonly Typeface NumberFace = new(new FontFamily("Malgun Gothic"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly CultureInfo Korean = CultureInfo.GetCultureInfo("ko-KR");

    /// <param name="effects">모자이크·블러를 그릴 원본 효과 캐시. null이면 효과는 그리지 않는다.</param>
    public static void Draw(DrawingContext dc, Annotation annotation, EffectCache? effects)
    {
        switch (annotation)
        {
            case MosaicAnnotation or BlurAnnotation:
                if (effects?.Get(annotation) is { } effect) dc.DrawImage(effect.Image, effect.Area);
                break;
            case StrokeAnnotation s: DrawStroke(dc, s); break;
            case ShapeAnnotation s: DrawShape(dc, s); break;
            case LineAnnotation l: DrawLine(dc, l); break;
            case TextAnnotation t: DrawText(dc, t); break;
            case NumberAnnotation n: DrawNumber(dc, n); break;
        }
    }

    /// <summary>옮기는 중인 객체를 (dx, dy)만큼 옮겨 그린다. 효과는 다시 계산하지 않고 원래 자리의 결과를 옮긴다.</summary>
    public static void DrawMoving(DrawingContext dc, Annotation annotation, double dx, double dy, EffectCache? effects)
    {
        if (annotation is MosaicAnnotation or BlurAnnotation)
        {
            if (effects?.GetShifted(annotation, dx, dy) is { } effect) dc.DrawImage(effect.Image, effect.Area);
            return;
        }
        Draw(dc, annotation.Offset(dx, dy), effects);
    }

    public static (double Width, double Height) MeasureText(string text, double fontSize)
    {
        var ft = MakeText(text, fontSize, Brushes.Black, TextFace);
        return (ft.WidthIncludingTrailingWhitespace, ft.Height);
    }

    public static Color ToColor(uint argb) => Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    public static Brush BrushOf(uint argb)
    {
        var brush = new SolidColorBrush(ToColor(argb));
        brush.Freeze();
        return brush;
    }

    private static Pen PenOf(uint argb, double width, bool dashed)
    {
        var pen = new Pen(BrushOf(argb), width) { LineJoin = PenLineJoin.Round };
        if (dashed)
        {
            // WPF 대시 길이는 굵기의 배수: 대시 = 굵기 × 3, 간격 = 굵기 × 2
            pen.DashStyle = new DashStyle([3.0, 2.0], 0);
            pen.DashCap = PenLineCap.Flat;
        }
        else
        {
            pen.StartLineCap = PenLineCap.Round;
            pen.EndLineCap = PenLineCap.Round;
        }
        pen.Freeze();
        return pen;
    }

    private static void DrawStroke(DrawingContext dc, StrokeAnnotation s)
    {
        if (s.Points.Count == 0) return;
        var argb = s.Highlighter ? (s.Style.ColorArgb & 0x00FFFFFF) | ((uint)DrawingOptions.HighlighterAlpha << 24) : s.Style.ColorArgb;
        if (s.Points.Count == 1)
        {
            dc.DrawEllipse(BrushOf(argb), null, ToPoint(s.Points[0]), s.DrawnWidth / 2, s.DrawnWidth / 2);
            return;
        }
        // 한 경로로 그려야 형광펜이 자기 자신과 겹친 곳에서 진해지지 않는다.
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(ToPoint(s.Points[0]), isFilled: false, isClosed: false);
            for (var i = 1; i < s.Points.Count; i++) ctx.LineTo(ToPoint(s.Points[i]), isStroked: true, isSmoothJoin: true);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, PenOf(argb, s.DrawnWidth, s.Style.Dashed), geometry);
    }

    private static void DrawShape(DrawingContext dc, ShapeAnnotation s)
    {
        var rect = new Rect(s.Rect.X, s.Rect.Y, s.Rect.Width, s.Rect.Height);
        var (brush, pen) = s.Filled ? (BrushOf(s.Style.ColorArgb), (Pen?)null) : ((Brush?)null, PenOf(s.Style.ColorArgb, s.Style.Width, s.Style.Dashed));
        if (s.Kind == ShapeKind.Rectangle)
            dc.DrawRectangle(brush, pen, rect);
        else
            dc.DrawEllipse(brush, pen, new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2), rect.Width / 2, rect.Height / 2);
    }

    private static void DrawLine(DrawingContext dc, LineAnnotation l)
    {
        var pen = PenOf(l.Style.ColorArgb, l.Style.Width, l.Style.Dashed);
        if (!l.Arrow)
        {
            dc.DrawLine(pen, ToPoint(l.Start), ToPoint(l.End));
            return;
        }
        var dx = l.End.X - l.Start.X;
        var dy = l.End.Y - l.Start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length == 0) return;
        var (ux, uy) = (dx / length, dy / length);
        var headLength = Math.Min(l.HeadLength, length);
        var baseCenter = new Point(l.End.X - ux * headLength, l.End.Y - uy * headLength);
        // 선은 머리 밑변까지만 그려 둥근 끝이 머리 밖으로 튀어나오지 않게 한다.
        dc.DrawLine(pen, ToPoint(l.Start), baseCenter);

        var half = l.HeadWidth / 2;
        var head = new StreamGeometry();
        using (var ctx = head.Open())
        {
            ctx.BeginFigure(ToPoint(l.End), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(baseCenter.X - uy * half, baseCenter.Y + ux * half), true, false);
            ctx.LineTo(new Point(baseCenter.X + uy * half, baseCenter.Y - ux * half), true, false);
        }
        head.Freeze();
        dc.DrawGeometry(BrushOf(l.Style.ColorArgb), null, head);
    }

    private static void DrawText(DrawingContext dc, TextAnnotation t)
    {
        if (string.IsNullOrEmpty(t.Text)) return;
        dc.DrawText(MakeText(t.Text, t.FontSize, BrushOf(t.ColorArgb), TextFace), ToPoint(t.Position));
    }

    private static void DrawNumber(DrawingContext dc, NumberAnnotation n)
    {
        var center = ToPoint(n.Center);
        dc.DrawEllipse(BrushOf(n.ColorArgb), null, center, n.Diameter / 2, n.Diameter / 2);
        var ft = MakeText(n.Number.ToString(CultureInfo.InvariantCulture), n.Diameter * 0.55, IsLight(n.ColorArgb) ? Brushes.Black : Brushes.White, NumberFace);
        dc.DrawText(ft, new Point(center.X - ft.Width / 2, center.Y - ft.Height / 2));
    }

    /// <summary>흰색·노랑처럼 밝은 원에는 검은 숫자를 쓴다.</summary>
    private static bool IsLight(uint argb)
    {
        var c = ToColor(argb);
        return c.R * 0.299 + c.G * 0.587 + c.B * 0.114 > 186;
    }

    private static FormattedText MakeText(string text, double size, Brush brush, Typeface face) =>
        new(text, Korean, FlowDirection.LeftToRight, face, size, brush, pixelsPerDip: 1.0);

    private static Point ToPoint(PointD p) => new(p.X, p.Y);
}
