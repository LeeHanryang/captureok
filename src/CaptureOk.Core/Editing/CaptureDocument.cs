using CaptureOk.Core.Geometry;

namespace CaptureOk.Core.Editing;

/// <summary>
/// 캡처 한 장의 편집 상태: 그린 객체 목록, 실행 취소 기록, 저장·복사 이후 바뀌었는지 여부.
/// 원본 이미지 픽셀은 앱이 따로 보관한다 (Core는 이미지 형식에 의존하지 않는다).
/// </summary>
public sealed class CaptureDocument(Guid id, int imageWidth, int imageHeight, DateTime createdAt)
{
    private const int NeverSaved = -1;

    private readonly List<Annotation> _annotations = [];
    private readonly List<(IEditCommand Command, int Version)> _undo = [];
    private readonly Stack<(IEditCommand Command, int Version)> _redo = new();
    private int _nextVersion = 1;
    private int _savedVersion = NeverSaved;

    public Guid Id { get; } = id;
    public int ImageWidth { get; } = imageWidth;
    public int ImageHeight { get; } = imageHeight;
    public DateTime CreatedAt { get; } = createdAt;
    public bool IsPinned { get; set; }

    public IReadOnlyList<Annotation> Annotations => _annotations;

    /// <summary>자른 영역 (원본 이미지 픽셀). null이면 자르지 않음. 객체 좌표는 자르기와 상관없이 원본 기준.</summary>
    public RectD? CropRect { get; private set; }

    /// <summary>화면·내보내기에서 보여 줄 원본 영역.</summary>
    public RectD ViewRect => CropRect ?? new RectD(0, 0, ImageWidth, ImageHeight);
    public int ViewWidth => (int)Math.Round(ViewRect.Width);
    public int ViewHeight => (int)Math.Round(ViewRect.Height);

    internal void SetCropRect(RectD? rect) => CropRect = rect;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>아직 저장·복사하지 않았거나, 그 뒤로 내용이 바뀌었다.</summary>
    public bool IsDirty => CurrentVersion != _savedVersion;

    public event Action? Changed;

    /// <summary>그림(객체·자르기)이 바뀔 때마다 늘어나는 번호. 저장·복사 표시만 바뀌면 그대로 (썸네일을 다시 만들지 판단).</summary>
    public int ContentVersion { get; private set; }

    private int CurrentVersion => _undo.Count == 0 ? 0 : _undo[^1].Version;

    public void Execute(IEditCommand command)
    {
        command.Apply(this);
        _undo.Add((command, _nextVersion++));
        _redo.Clear();
        ContentVersion++;
        Changed?.Invoke();
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        var entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        entry.Command.Revert(this);
        _redo.Push(entry);
        ContentVersion++;
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        var entry = _redo.Pop();
        entry.Command.Apply(this);
        _undo.Add(entry);
        ContentVersion++;
        Changed?.Invoke();
    }

    public void MarkSaved()
    {
        _savedVersion = CurrentVersion;
        Changed?.Invoke();
    }

    /// <summary>보관해 둔 고정 캡처를 되살린다. 실행 취소 기록은 없고, isDirty가 false면 저장된 상태로 시작한다.</summary>
    internal static CaptureDocument Restore(Guid id, int width, int height, DateTime createdAt, RectD? crop,
        IEnumerable<Annotation> annotations, bool isDirty)
    {
        var doc = new CaptureDocument(id, width, height, createdAt) { IsPinned = true, CropRect = crop };
        doc._annotations.AddRange(annotations);
        if (!isDirty) doc._savedVersion = doc.CurrentVersion;
        return doc;
    }

    public Annotation? Find(Guid id) => _annotations.FirstOrDefault(a => a.Id == id);

    /// <returns>바뀌기 전 객체. 그 Id가 없으면 null (아무것도 바꾸지 않음).</returns>
    internal Annotation? ReplaceAnnotation(Annotation replacement)
    {
        var index = _annotations.FindIndex(a => a.Id == replacement.Id);
        if (index < 0) return null;
        var original = _annotations[index];
        _annotations[index] = replacement;
        return original;
    }

    internal void InsertAnnotation(int index, Annotation annotation) =>
        _annotations.Insert(Math.Clamp(index, 0, _annotations.Count), annotation);

    /// <returns>지운 위치. 없으면 -1.</returns>
    internal int RemoveAnnotationAt(Guid id)
    {
        var index = _annotations.FindIndex(a => a.Id == id);
        if (index >= 0) _annotations.RemoveAt(index);
        return index;
    }
}
