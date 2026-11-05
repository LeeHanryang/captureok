using CaptureOk.Core.Editing;

namespace CaptureOk.Core.History;

/// <summary>고정·고정 해제 결과. Failed는 앱이 보관 파일을 쓰지 못해 고정을 되돌렸을 때.</summary>
public enum PinResult { Pinned, Unpinned, LimitReached, NotFound, Failed }

/// <summary>최근 캡처 목록. 최신이 맨 앞이고, 최대 개수를 넘으면 고정하지 않은 가장 오래된 것부터 뺀다.</summary>
public sealed class CaptureHistory(int maxCount)
{
    private readonly List<CaptureDocument> _items = [];
    private int _maxCount = maxCount;

    public IReadOnlyList<CaptureDocument> Items => _items;
    public CaptureDocument? Current { get; private set; }
    public int UnsavedCount => _items.Count(d => d.IsDirty);

    /// <summary>앱을 끄면 사라지는 저장 안 한 캡처 수 (고정 캡처는 다음 실행에 편집 상태째 돌아오므로 빼고 센다).</summary>
    public int LostOnExitCount => _items.Count(d => d.IsDirty && !d.IsPinned);

    public event Action? Changed;

    public int MaxCount
    {
        get => _maxCount;
        set => SetMaxCount(value);
    }

    /// <summary>최대 개수를 바꾼다.</summary>
    /// <returns>줄어서 목록에서 빠진 문서들 (호출자가 이미지를 해제하고, 저장 안 한 것이 있으면 알린다)</returns>
    public IReadOnlyList<CaptureDocument> SetMaxCount(int maxCount)
    {
        _maxCount = maxCount;
        var evicted = Trim();
        if (evicted.Count > 0) Changed?.Invoke();
        return evicted;
    }

    /// <returns>최대 개수를 넘어 목록에서 빠진 문서들 (호출자가 이미지를 해제한다)</returns>
    public IReadOnlyList<CaptureDocument> Add(CaptureDocument doc)
    {
        _items.Insert(0, doc);
        Current = doc;
        var evicted = Trim();
        Changed?.Invoke();
        return evicted;
    }

    public void Select(Guid id)
    {
        var doc = _items.FirstOrDefault(d => d.Id == id);
        if (doc is null || ReferenceEquals(doc, Current)) return;
        Current = doc;
        Changed?.Invoke();
    }

    public bool Remove(Guid id)
    {
        var index = _items.FindIndex(d => d.Id == id);
        if (index < 0) return false;
        var removed = _items[index];
        _items.RemoveAt(index);
        if (ReferenceEquals(removed, Current))
            Current = _items.Count == 0 ? null : _items[Math.Min(index, _items.Count - 1)];
        Changed?.Invoke();
        return true;
    }

    /// <summary>고정은 이 개수까지 (고정 캡처는 앱을 껐다 켜도 남는다).</summary>
    public const int MaxPinned = 10;

    public int PinnedCount => _items.Count(d => d.IsPinned);

    /// <summary>고정·고정 해제. 이미 MaxPinned개면 더 고정하지 않고 LimitReached.</summary>
    public PinResult SetPinned(Guid id, bool pinned)
    {
        var doc = _items.FirstOrDefault(d => d.Id == id);
        if (doc is null) return PinResult.NotFound;
        if (doc.IsPinned == pinned) return pinned ? PinResult.Pinned : PinResult.Unpinned;
        if (pinned && PinnedCount >= MaxPinned) return PinResult.LimitReached;
        doc.IsPinned = pinned;
        Changed?.Invoke();
        return pinned ? PinResult.Pinned : PinResult.Unpinned;
    }

    /// <summary>목록 비우기: 고정하지 않은 캡처를 모두 뺀다. 지금 문서가 빠지면 남은 첫 캡처(없으면 없음)로.</summary>
    /// <returns>뺀 문서들 (호출자가 이미지를 해제한다)</returns>
    public IReadOnlyList<CaptureDocument> ClearUnpinned() => ClearUnpinned(_items.Select(d => d.Id).ToList());

    /// <summary>
    /// 확인 창에 보여 준 캡처(ids)만 비운다. 확인하는 동안 새로 들어온 캡처나 그 사이 고정한 캡처는 남긴다.
    /// </summary>
    public IReadOnlyList<CaptureDocument> ClearUnpinned(IReadOnlyCollection<Guid> ids)
    {
        bool Removable(CaptureDocument d) => !d.IsPinned && ids.Contains(d.Id);
        var removed = _items.Where(Removable).ToList();
        if (removed.Count == 0) return removed;
        _items.RemoveAll(Removable);
        if (Current is null || !_items.Contains(Current)) Current = _items.FirstOrDefault();
        Changed?.Invoke();
        return removed;
    }

    /// <summary>앱을 시작할 때 보관해 둔 고정 캡처를 넣는다. 만든 시각 최신순으로, 지금 목록 뒤에 붙인다.</summary>
    public void Restore(IEnumerable<CaptureDocument> docs)
    {
        var added = docs.Where(d => _items.All(x => x.Id != d.Id)).OrderByDescending(d => d.CreatedAt).ToList();
        if (added.Count == 0) return;
        _items.AddRange(added);
        Current ??= _items[0];
        Changed?.Invoke();
    }

    private List<CaptureDocument> Trim()
    {
        var evicted = new List<CaptureDocument>();
        while (_items.Count > _maxCount)
        {
            var oldestUnpinned = _items.LastOrDefault(d => !d.IsPinned && !ReferenceEquals(d, Current));
            if (oldestUnpinned is null) break; // 모두 고정이면 더 줄이지 않는다
            _items.Remove(oldestUnpinned);
            evicted.Add(oldestUnpinned);
        }
        return evicted;
    }
}
