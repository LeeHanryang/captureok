using CaptureOk.Core.Geometry;

namespace CaptureOk.Core.Editing;

/// <summary>문서를 바꾸는 편집 한 번. 실행 취소를 위해 되돌릴 수 있어야 한다.</summary>
public interface IEditCommand
{
    void Apply(CaptureDocument doc);
    void Revert(CaptureDocument doc);
}

public sealed class AddAnnotation(Annotation annotation) : IEditCommand
{
    public void Apply(CaptureDocument doc) => doc.InsertAnnotation(doc.Annotations.Count, annotation);
    public void Revert(CaptureDocument doc) => doc.RemoveAnnotationAt(annotation.Id);
}

public sealed class RemoveAnnotation(Guid id) : IEditCommand
{
    private Annotation? _removed;
    private int _index;

    public void Apply(CaptureDocument doc)
    {
        _removed = doc.Annotations.FirstOrDefault(a => a.Id == id);
        _index = doc.RemoveAnnotationAt(id);
    }

    public void Revert(CaptureDocument doc)
    {
        if (_removed is not null && _index >= 0) doc.InsertAnnotation(_index, _removed);
    }
}

/// <summary>여러 객체를 한 번에 지운다 (지우개 드래그 한 번 = 실행 취소 한 번). 되돌리면 원래 겹침 순서로 돌아온다.</summary>
public sealed class RemoveAnnotations(IReadOnlyCollection<Guid> ids) : IEditCommand
{
    private readonly List<(int Index, Annotation Annotation)> _removed = [];

    public void Apply(CaptureDocument doc)
    {
        _removed.Clear();
        for (var i = 0; i < doc.Annotations.Count; i++)
            if (ids.Contains(doc.Annotations[i].Id)) _removed.Add((i, doc.Annotations[i]));
        foreach (var (_, a) in _removed) doc.RemoveAnnotationAt(a.Id);
    }

    public void Revert(CaptureDocument doc)
    {
        foreach (var (index, a) in _removed) doc.InsertAnnotation(index, a); // 앞에서부터 넣으면 원래 위치가 된다
    }
}

/// <summary>같은 Id의 객체를 같은 위치에서 바꾼다 (이동·텍스트 수정). 그 Id가 없으면 아무것도 하지 않는다.</summary>
public sealed class ReplaceAnnotation(Annotation replacement) : IEditCommand
{
    private Annotation? _original;

    public void Apply(CaptureDocument doc) => _original = doc.ReplaceAnnotation(replacement);

    public void Revert(CaptureDocument doc)
    {
        if (_original is not null) doc.ReplaceAnnotation(_original);
    }
}

public static class EditCommands
{
    /// <summary>객체를 (dx, dy)만큼 옮기는 명령. 객체가 없으면 아무것도 하지 않는 교체 명령.</summary>
    public static IEditCommand Move(CaptureDocument doc, Guid id, double dx, double dy) =>
        doc.Find(id) is { } current ? new ReplaceAnnotation(current.Offset(dx, dy)) : new ReplaceAnnotation(new MissingAnnotation(id));

    private sealed record MissingAnnotation(Guid Id) : Annotation(Id)
    {
        public override RectD Bounds => default;
        public override Annotation Offset(double dx, double dy) => this;
    }
}

/// <summary>자르기 영역을 바꾼다. 원본 이미지 경계로 자르고, 1px 미만이면 자르기를 없앤다.</summary>
public sealed class SetCrop(RectD? rect) : IEditCommand
{
    private RectD? _previous;

    public void Apply(CaptureDocument doc)
    {
        _previous = doc.CropRect;
        // 픽셀 경계에 맞춘다 (내보낸 이미지 가장자리가 흐려지지 않게).
        doc.SetCropRect(rect is { } r ? CropEditing.Normalize(r, doc.ImageWidth, doc.ImageHeight) : null);
    }

    public void Revert(CaptureDocument doc) => doc.SetCropRect(_previous);
}

public sealed class ClearAnnotations : IEditCommand
{
    private List<Annotation> _removed = [];

    public void Apply(CaptureDocument doc)
    {
        _removed = doc.Annotations.ToList();
        foreach (var a in _removed) doc.RemoveAnnotationAt(a.Id);
    }

    public void Revert(CaptureDocument doc)
    {
        for (var i = 0; i < _removed.Count; i++) doc.InsertAnnotation(i, _removed[i]);
    }
}
