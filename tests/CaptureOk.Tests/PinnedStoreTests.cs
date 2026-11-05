using System.Text.Json;
using CaptureOk.Core.Editing;
using CaptureOk.Core.Geometry;
using CaptureOk.Core.History;

namespace CaptureOk.Tests;

public sealed class PinnedStoreTests : IDisposable
{
    private static readonly StrokeStyle Red3 = new(0xFFFF0000, 3, Dashed: false);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "captureok-pinned-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static CaptureDocument Pinned(DateTime createdAt)
    {
        var doc = new CaptureDocument(Guid.NewGuid(), 640, 480, createdAt) { IsPinned = true };
        return doc;
    }

    private static List<Annotation> OneOfEach() =>
    [
        new StrokeAnnotation(Guid.NewGuid(), [new PointD(1, 2), new PointD(3.5, 4.25)], Red3, Highlighter: true),
        new ShapeAnnotation(Guid.NewGuid(), ShapeKind.Ellipse, new RectD(5, 6, 70, 80), Red3 with { Dashed = true }, Filled: false),
        new LineAnnotation(Guid.NewGuid(), new PointD(0, 0), new PointD(100, 50), Red3, Arrow: true),
        new TextAnnotation(Guid.NewGuid(), new PointD(10, 10), "안녕\n둘째 줄", 0xFF00FF00, 20, 80, 52),
        new NumberAnnotation(Guid.NewGuid(), new PointD(30, 40), 3, 0xFF0000FF, 28),
        new MosaicAnnotation(Guid.NewGuid(), new RectD(100, 100, 50, 40), 12),
        new BlurAnnotation(Guid.NewGuid(), new RectD(200, 100, 60, 30), 8),
    ];

    [Fact(DisplayName = "json-round-trips-every-annotation-type")]
    public void JsonRoundTripsEveryAnnotationType()
    {
        var doc = Pinned(new DateTime(2026, 10, 27, 21, 30, 15));
        foreach (var a in OneOfEach()) doc.Execute(new AddAnnotation(a));
        doc.Execute(new SetCrop(new RectD(10, 20, 300, 200)));

        var back = DocumentJson.Deserialize(DocumentJson.Serialize(doc));

        Assert.Equal(doc.Id, back.Id);
        Assert.Equal((640, 480), (back.ImageWidth, back.ImageHeight));
        Assert.Equal(doc.CreatedAt, back.CreatedAt);
        Assert.Equal(new RectD(10, 20, 300, 200), back.CropRect);
        Assert.Equal(doc.Annotations, back.Annotations); // StrokeAnnotation은 점 목록까지 비교
        Assert.True(back.IsPinned);
        Assert.False(back.CanUndo);
    }

    [Fact(DisplayName = "json-keeps-saved-state")]
    public void JsonKeepsSavedState()
    {
        var saved = Pinned(DateTime.Now);
        saved.Execute(new AddAnnotation(OneOfEach()[0]));
        saved.MarkSaved();
        Assert.False(DocumentJson.Deserialize(DocumentJson.Serialize(saved)).IsDirty);

        var unsaved = Pinned(DateTime.Now);
        Assert.True(DocumentJson.Deserialize(DocumentJson.Serialize(unsaved)).IsDirty);
    }

    [Fact(DisplayName = "json-rejects-unknown-version")]
    public void JsonRejectsUnknownVersion()
    {
        var json = DocumentJson.Serialize(Pinned(DateTime.Now)).Replace("\"version\": 1", "\"version\": 99");
        Assert.Contains("99", json);
        Assert.Throws<JsonException>(() => DocumentJson.Deserialize(json));
    }

    private void WriteFolder(CaptureDocument doc, bool withBase = true)
    {
        var store = new PinnedStore(_root);
        store.WriteDocument(doc);
        if (withBase) File.WriteAllBytes(store.BasePathOf(doc.Id), []);
    }

    [Fact(DisplayName = "load-skips-broken-folders")]
    public void LoadSkipsBrokenFolders()
    {
        var store = new PinnedStore(_root);
        var good = Pinned(new DateTime(2026, 10, 27, 9, 0, 0));
        WriteFolder(good);
        var broken = Pinned(new DateTime(2026, 10, 27, 9, 1, 0));
        WriteFolder(broken);
        File.WriteAllText(Path.Combine(store.FolderOf(broken.Id), "document.json"), "{ 깨짐");
        var noBase = Pinned(new DateTime(2026, 10, 27, 9, 2, 0));
        WriteFolder(noBase, withBase: false);

        var logs = new List<string>();
        var loaded = store.LoadAll(logs.Add);

        Assert.Equal([good.Id], loaded.Select(d => d.Id));
        Assert.Equal(2, logs.Count);
    }

    [Fact(DisplayName = "load-returns-newest-ten")]
    public void LoadReturnsNewestTen()
    {
        var docs = Enumerable.Range(1, 12).Select(i => Pinned(new DateTime(2026, 10, 27, 9, i, 0))).ToList();
        foreach (var d in docs) WriteFolder(d);

        var loaded = new PinnedStore(_root).LoadAll(_ => { });

        Assert.Equal(docs.AsEnumerable().Reverse().Take(10).Select(d => d.Id), loaded.Select(d => d.Id));
    }

    [Fact(DisplayName = "load-from-missing-root-is-empty")]
    public void LoadFromMissingRootIsEmpty() => Assert.Empty(new PinnedStore(_root).LoadAll(_ => { }));

    [Fact(DisplayName = "delete-removes-folder")]
    public void DeleteRemovesFolder()
    {
        var store = new PinnedStore(_root);
        var doc = Pinned(DateTime.Now);
        WriteFolder(doc);
        Assert.True(Directory.Exists(store.FolderOf(doc.Id)));

        store.Delete(doc.Id);
        Assert.False(Directory.Exists(store.FolderOf(doc.Id)));
        store.Delete(Guid.NewGuid()); // 없는 것은 그냥 넘어간다
    }

    [Fact(DisplayName = "delete-of-locked-folder-still-stops-restore")]
    public void DeleteOfLockedFolderStillStopsRestore()
    {
        var store = new PinnedStore(_root);
        var doc = Pinned(DateTime.Now);
        WriteFolder(doc);

        using (new FileStream(store.BasePathOf(doc.Id), FileMode.Open, FileAccess.Read, FileShare.None)) // 백신·탐색기가 잡고 있는 상황
        {
            Assert.ThrowsAny<IOException>(() => store.Delete(doc.Id));
        }

        Assert.Empty(store.LoadAll(_ => { })); // 다음 실행에 고정 캡처로 되살아나지 않는다
        store.RemoveOrphans(_ => { });
        Assert.False(Directory.Exists(store.FolderOf(doc.Id))); // 다음 시작 때 남은 폴더도 정리
    }

    [Fact(DisplayName = "remove-orphans-deletes-only-half-folders")]
    public void RemoveOrphansDeletesOnlyHalfFolders()
    {
        var store = new PinnedStore(_root);
        var good = Pinned(DateTime.Now);
        WriteFolder(good);
        var noBase = Pinned(DateTime.Now);
        WriteFolder(noBase, withBase: false);
        var noDocument = Guid.NewGuid();
        Directory.CreateDirectory(store.FolderOf(noDocument));
        File.WriteAllBytes(store.BasePathOf(noDocument), []);
        var newerVersion = Pinned(DateTime.Now);
        WriteFolder(newerVersion);
        File.WriteAllText(Path.Combine(store.FolderOf(newerVersion.Id), "document.json"), "{ \"version\": 2 }"); // 새 버전이 쓴 것일 수 있어 지우지 않는다
        var other = Path.Combine(_root, "not-a-capture");
        Directory.CreateDirectory(other);

        var logs = new List<string>();
        store.RemoveOrphans(logs.Add);

        Assert.True(Directory.Exists(store.FolderOf(good.Id)));
        Assert.False(Directory.Exists(store.FolderOf(noBase.Id)));
        Assert.False(Directory.Exists(store.FolderOf(noDocument)));
        Assert.True(Directory.Exists(store.FolderOf(newerVersion.Id)));
        Assert.True(Directory.Exists(other));
        Assert.Equal(2, logs.Count);
    }
}