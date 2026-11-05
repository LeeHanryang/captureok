using System.Text.Json.Serialization;
using CaptureOk.Core.Geometry;

namespace CaptureOk.Core.Editing;

/// <summary>선 모양: 색(ARGB), 굵기(이미지 픽셀), 점선 여부.</summary>
public readonly record struct StrokeStyle(uint ColorArgb, double Width, bool Dashed);

/// <summary>펜·형광펜 획.</summary>
public sealed record StrokeAnnotation(Guid Id, IReadOnlyList<PointD> Points, StrokeStyle Style, bool Highlighter) : Annotation(Id)
{
    /// <summary>실제로 그리는 굵기 (형광펜은 선택 굵기의 4배).</summary>
    [JsonIgnore]
    public double DrawnWidth => Highlighter ? Style.Width * DrawingOptions.HighlighterWidthFactor : Style.Width;

    [JsonIgnore]

    public override RectD Bounds => PointMath.BoundsOf(Points).Inflate(DrawnWidth / 2);

    public override Annotation Offset(double dx, double dy) =>
        this with { Points = Points.Select(p => new PointD(p.X + dx, p.Y + dy)).ToArray() };

    public bool Equals(StrokeAnnotation? other) =>
        other is not null && Id == other.Id && Style == other.Style && Highlighter == other.Highlighter && Points.SequenceEqual(other.Points);

    public override int GetHashCode() => HashCode.Combine(Id, Style, Highlighter, Points.Count);
}

public enum ShapeKind { Rectangle, Ellipse }

/// <summary>사각형·원. Filled면 테두리 없이 같은 색으로 채운다.</summary>
public sealed record ShapeAnnotation(Guid Id, ShapeKind Kind, RectD Rect, StrokeStyle Style, bool Filled) : Annotation(Id)
{
    [JsonIgnore]
    public override RectD Bounds => Filled ? Rect : Rect.Inflate(Style.Width / 2);

    public override Annotation Offset(double dx, double dy) => this with { Rect = Rect with { X = Rect.X + dx, Y = Rect.Y + dy } };
}

/// <summary>직선·화살표 (화살표는 End 쪽에 머리).</summary>
public sealed record LineAnnotation(Guid Id, PointD Start, PointD End, StrokeStyle Style, bool Arrow) : Annotation(Id)
{
    [JsonIgnore]
    public double HeadLength => Math.Max(10, Style.Width * 4);
    [JsonIgnore]
    public double HeadWidth => HeadLength * 0.8;

    [JsonIgnore]

    public override RectD Bounds =>
        RectD.FromPoints(Start, End).Inflate(Arrow ? Math.Max(Style.Width / 2, HeadWidth / 2) : Style.Width / 2);

    public override Annotation Offset(double dx, double dy) =>
        this with { Start = new PointD(Start.X + dx, Start.Y + dy), End = new PointD(End.X + dx, End.Y + dy) };
}

/// <summary>텍스트. Width·Height는 앱이 글꼴로 측정해 넣는다.</summary>
public sealed record TextAnnotation(Guid Id, PointD Position, string Text, uint ColorArgb, double FontSize, double Width, double Height) : Annotation(Id)
{
    [JsonIgnore]
    public override RectD Bounds => new(Position.X, Position.Y, Width, Height);

    public override Annotation Offset(double dx, double dy) => this with { Position = new PointD(Position.X + dx, Position.Y + dy) };
}

/// <summary>원 안의 순서 번호.</summary>
public sealed record NumberAnnotation(Guid Id, PointD Center, int Number, uint ColorArgb, double Diameter) : Annotation(Id)
{
    [JsonIgnore]
    public override RectD Bounds => new(Center.X - Diameter / 2, Center.Y - Diameter / 2, Diameter, Diameter);

    public override Annotation Offset(double dx, double dy) => this with { Center = new PointD(Center.X + dx, Center.Y + dy) };
}

/// <summary>모자이크 영역. 원본 이미지의 그 영역을 BlockSize 칸 평균색으로 덮는다.</summary>
public sealed record MosaicAnnotation(Guid Id, RectD Rect, int BlockSize) : Annotation(Id)
{
    [JsonIgnore]
    public override RectD Bounds => Rect;

    public override Annotation Offset(double dx, double dy) => this with { Rect = Rect with { X = Rect.X + dx, Y = Rect.Y + dy } };
}

/// <summary>블러 영역. 원본 이미지의 그 영역을 Radius 상자 블러(3회)로 흐리게 덮는다.</summary>
public sealed record BlurAnnotation(Guid Id, RectD Rect, int Radius) : Annotation(Id)
{
    [JsonIgnore]
    public override RectD Bounds => Rect;

    public override Annotation Offset(double dx, double dy) => this with { Rect = Rect with { X = Rect.X + dx, Y = Rect.Y + dy } };
}

internal static class PointMath
{
    public static RectD BoundsOf(IReadOnlyList<PointD> points)
    {
        if (points.Count == 0) return default;
        double left = points.Min(p => p.X), top = points.Min(p => p.Y);
        return new RectD(left, top, points.Max(p => p.X) - left, points.Max(p => p.Y) - top);
    }
}
