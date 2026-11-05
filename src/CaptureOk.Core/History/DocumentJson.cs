using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CaptureOk.Core.Editing;
using CaptureOk.Core.Geometry;

namespace CaptureOk.Core.History;

/// <summary>고정 캡처의 document.json: 크기·만든 시각·자르기·저장 여부·편집 객체. 실행 취소 기록은 남기지 않는다.</summary>
public static class DocumentJson
{
    private const int Version = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private sealed record Dto(int Version, Guid Id, int Width, int Height, DateTime CreatedAt, RectD? Crop, bool IsDirty, List<Annotation> Annotations);

    public static string Serialize(CaptureDocument doc) => JsonSerializer.Serialize(
        new Dto(Version, doc.Id, doc.ImageWidth, doc.ImageHeight, doc.CreatedAt, doc.CropRect, doc.IsDirty, [.. doc.Annotations]), Options);

    /// <exception cref="JsonException">형식이 깨졌거나 모르는 버전·객체 종류</exception>
    public static CaptureDocument Deserialize(string json)
    {
        var version = JsonNode.Parse(json)?["version"]?.GetValue<int>();
        if (version != Version) throw new JsonException($"알 수 없는 document.json 버전: {version?.ToString() ?? "없음"}");
        var dto = JsonSerializer.Deserialize<Dto>(json, Options) ?? throw new JsonException("빈 document.json");
        if (dto.Width <= 0 || dto.Height <= 0) throw new JsonException($"잘못된 크기: {dto.Width}×{dto.Height}");
        return CaptureDocument.Restore(dto.Id, dto.Width, dto.Height, dto.CreatedAt, dto.Crop, dto.Annotations ?? [], dto.IsDirty);
    }
}
