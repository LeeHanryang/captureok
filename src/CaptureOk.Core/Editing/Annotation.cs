using System.Text.Json.Serialization;
using CaptureOk.Core.Geometry;

namespace CaptureOk.Core.Editing;

/// <summary>캡처 위에 그린 객체 하나. 좌표·굵기·크기는 모두 원본 이미지 픽셀 기준.</summary>
/// <remarks>고정 캡처의 document.json에는 "type" 이름으로 종류를 적는다 (이름을 바꾸면 보관한 캡처를 못 읽는다).</remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(StrokeAnnotation), "stroke")]
[JsonDerivedType(typeof(ShapeAnnotation), "shape")]
[JsonDerivedType(typeof(LineAnnotation), "line")]
[JsonDerivedType(typeof(TextAnnotation), "text")]
[JsonDerivedType(typeof(NumberAnnotation), "number")]
[JsonDerivedType(typeof(MosaicAnnotation), "mosaic")]
[JsonDerivedType(typeof(BlurAnnotation), "blur")]
public abstract record Annotation(Guid Id)
{
    /// <summary>그려지는 영역 전체 (굵기·화살표 머리 포함).</summary>
    [JsonIgnore]
    public abstract RectD Bounds { get; }

    /// <summary>같은 Id로 (dx, dy)만큼 옮긴 사본.</summary>
    public abstract Annotation Offset(double dx, double dy);
}
