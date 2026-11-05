using CaptureOk.Core.Editing;
using CaptureOk.Core.History;

namespace CaptureOk.Tests;

public class CaptureHistoryTests
{
    private static CaptureDocument Doc(int minute) => new(Guid.NewGuid(), 10, 10, new DateTime(2026, 10, 27, 9, minute, 0));

    [Fact(DisplayName = "add-puts-newest-first-and-current")]
    public void AddPutsNewestFirstAndCurrent()
    {
        var history = new CaptureHistory(30);
        CaptureDocument a = Doc(1), b = Doc(2);
        history.Add(a);
        history.Add(b);

        Assert.Equal([b, a], history.Items);
        Assert.Same(b, history.Current);
    }

    [Fact(DisplayName = "evicts-oldest-unpinned")]
    public void EvictsOldestUnpinned()
    {
        var history = new CaptureHistory(5);
        var docs = Enumerable.Range(1, 6).Select(Doc).ToList();
        docs[0].IsPinned = true;

        IReadOnlyList<CaptureDocument> evicted = [];
        foreach (var d in docs) evicted = history.Add(d);

        Assert.Equal([docs[1]], evicted);
        Assert.Contains(docs[0], history.Items);
        Assert.Equal(5, history.Items.Count);
    }

    [Fact(DisplayName = "shrinking-max-evicts")]
    public void ShrinkingMaxEvicts()
    {
        var history = new CaptureHistory(30);
        foreach (var d in Enumerable.Range(1, 6).Select(Doc)) history.Add(d);

        history.MaxCount = 5;

        Assert.Equal(5, history.Items.Count);
    }

    [Fact(DisplayName = "remove-current-selects-next")]
    public void RemoveCurrentSelectsNext()
    {
        var history = new CaptureHistory(30);
        CaptureDocument a = Doc(1), b = Doc(2), c = Doc(3);
        history.Add(a);
        history.Add(b);
        history.Add(c);

        Assert.True(history.Remove(c.Id));

        Assert.Same(b, history.Current);
    }

    [Fact(DisplayName = "select-switches-current")]
    public void SelectSwitchesCurrent()
    {
        var history = new CaptureHistory(30);
        CaptureDocument a = Doc(1), b = Doc(2);
        history.Add(a);
        history.Add(b);

        history.Select(a.Id);

        Assert.Same(a, history.Current);
    }

    [Fact(DisplayName = "unsaved-count")]
    public void UnsavedCount()
    {
        var history = new CaptureHistory(30);
        var docs = Enumerable.Range(1, 3).Select(Doc).ToList();
        foreach (var d in docs) history.Add(d);

        docs[1].MarkSaved();

        Assert.Equal(2, history.UnsavedCount);
    }

    private static CaptureHistory WithDocs(int count, out List<CaptureDocument> docs)
    {
        var history = new CaptureHistory(30);
        docs = Enumerable.Range(1, count).Select(Doc).ToList();
        foreach (var d in docs) history.Add(d);
        return history;
    }

    [Fact(DisplayName = "eleventh-pin-is-refused")]
    public void EleventhPinIsRefused()
    {
        var history = WithDocs(11, out var docs);
        foreach (var d in docs.Take(10)) Assert.Equal(PinResult.Pinned, history.SetPinned(d.Id, true));

        Assert.Equal(PinResult.LimitReached, history.SetPinned(docs[10].Id, true));
        Assert.False(docs[10].IsPinned);
        Assert.Equal(10, history.PinnedCount);
        Assert.Equal(10, CaptureHistory.MaxPinned);
    }

    [Fact(DisplayName = "unpin-frees-a-slot")]
    public void UnpinFreesASlot()
    {
        var history = WithDocs(11, out var docs);
        foreach (var d in docs.Take(10)) history.SetPinned(d.Id, true);

        Assert.Equal(PinResult.Unpinned, history.SetPinned(docs[0].Id, false));
        Assert.Equal(PinResult.Pinned, history.SetPinned(docs[10].Id, true));
    }

    [Fact(DisplayName = "pin-unknown-id")]
    public void PinUnknownId() =>
        Assert.Equal(PinResult.NotFound, new CaptureHistory(30).SetPinned(Guid.NewGuid(), true));

    [Fact(DisplayName = "remove-current-selects-neighbor")]
    public void RemoveCurrentSelectsNeighbor()
    {
        var history = WithDocs(3, out var docs); // Items = [c, b, a]
        history.Select(docs[1].Id);

        history.Remove(docs[1].Id);
        Assert.Same(docs[0], history.Current);

        history.Remove(docs[2].Id);
        history.Remove(docs[0].Id);
        Assert.Null(history.Current);
    }

    [Fact(DisplayName = "clear-keeps-pinned-and-moves-current")]
    public void ClearKeepsPinnedAndMovesCurrent()
    {
        var history = WithDocs(4, out var docs); // Items = [d, c, b, a], Current = d
        var (a, b, c, d) = (docs[0], docs[1], docs[2], docs[3]);
        history.SetPinned(b.Id, true);
        history.SetPinned(a.Id, true);

        Assert.Equal([d, c], history.ClearUnpinned());
        Assert.Equal([b, a], history.Items);
        Assert.Same(b, history.Current);

        history.SetPinned(b.Id, false);
        history.SetPinned(a.Id, false);
        history.ClearUnpinned();
        Assert.Empty(history.Items);
        Assert.Null(history.Current);
    }

    [Fact(DisplayName = "restore-orders-newest-first")]
    public void RestoreOrdersNewestFirst()
    {
        var history = new CaptureHistory(30);
        CaptureDocument m1 = Doc(1), m3 = Doc(3), m2 = Doc(2);

        history.Restore([m1, m3, m2]);
        Assert.Equal([m3, m2, m1], history.Items);
        Assert.Same(m3, history.Current);

        var fresh = Doc(9);
        history.Add(fresh);
        Assert.Same(fresh, history.Items[0]);
    }

    [Fact(DisplayName = "batch-names-are-unique")]
    public void BatchNamesAreUnique()
    {
        var folder = Path.Combine(Path.GetTempPath(), "captureok-batch");
        var docs = Enumerable.Range(0, 3).Select(_ => Doc(5)).ToList(); // 같은 시각

        var plan = BatchNames.Plan(docs, folder, "%Y%m%d_%H%M%S", ".png", _ => false);

        Assert.Equal(docs, plan.Select(p => p.Doc));
        Assert.Equal(3, plan.Select(p => p.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(plan, p =>
        {
            Assert.Equal(folder, Path.GetDirectoryName(p.Path));
            Assert.EndsWith(".png", p.Path);
        });
    }

    [Fact(DisplayName = "exit-loses-only-unpinned-unsaved")]
    public void ExitLosesOnlyUnpinnedUnsaved()
    {
        var history = WithDocs(3, out var docs);
        history.SetPinned(docs[0].Id, true);  // 고정 + 저장 안 함 → 다음 실행에 남으므로 묻지 않는다
        docs[2].MarkSaved();                  // 저장함

        Assert.Equal(2, history.UnsavedCount);
        Assert.Equal(1, history.LostOnExitCount);
    }

    [Fact(DisplayName = "clear-removes-only-the-confirmed-captures")]
    public void ClearRemovesOnlyTheConfirmedCaptures()
    {
        var history = WithDocs(2, out var docs);
        var confirmed = history.Items.Where(d => !d.IsPinned).Select(d => d.Id).ToList(); // 확인 창에 보여 준 것
        var arrived = Doc(30);
        history.Add(arrived); // 확인 창이 떠 있는 동안 들어온 새 캡처

        var removed = history.ClearUnpinned(confirmed);

        Assert.Equal(docs.AsEnumerable().Reverse(), removed);
        Assert.Equal([arrived], history.Items);
        Assert.Same(arrived, history.Current);
    }

    [Fact(DisplayName = "clear-never-removes-pinned-even-if-listed")]
    public void ClearNeverRemovesPinnedEvenIfListed()
    {
        var history = WithDocs(2, out var docs);
        var ids = docs.Select(d => d.Id).ToList();
        history.SetPinned(docs[0].Id, true); // 확인 뒤에 고정함

        history.ClearUnpinned(ids);

        Assert.Equal([docs[0]], history.Items);
    }

    [Fact(DisplayName = "set-max-count-returns-evicted")]
    public void SetMaxCountReturnsEvicted()
    {
        var history = WithDocs(7, out var docs); // Items = 최신 7 … 1, Current = 7
        docs[1].MarkSaved();

        var evicted = history.SetMaxCount(5);

        Assert.Equal([docs[0], docs[1]], evicted); // 가장 오래된 것부터
        Assert.Equal(5, history.MaxCount);
        Assert.Equal(5, history.Items.Count);
    }
}