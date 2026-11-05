using CaptureOk.Core.Editing;

namespace CaptureOk.Tests;

public class CaptureDocumentTests
{
    private sealed record Dot(Guid Id) : Annotation(Id)
    {
        public override CaptureOk.Core.Geometry.RectD Bounds => new(0, 0, 1, 1);
        public override Annotation Offset(double dx, double dy) => this;
    }

    private static CaptureDocument NewDoc() => new(Guid.NewGuid(), 100, 50, new DateTime(2026, 10, 27, 9, 0, 0));

    private static Dot NewDot() => new(Guid.NewGuid());

    [Fact(DisplayName = "new-document-is-dirty-and-empty")]
    public void NewDocumentIsDirtyAndEmpty()
    {
        var doc = NewDoc();
        Assert.True(doc.IsDirty);
        Assert.Empty(doc.Annotations);
        Assert.False(doc.CanUndo);
        Assert.False(doc.CanRedo);
        Assert.False(doc.IsPinned);
    }

    [Fact(DisplayName = "execute-add-then-undo-redo")]
    public void ExecuteAddThenUndoRedo()
    {
        var doc = NewDoc();
        var dot = NewDot();
        doc.Execute(new AddAnnotation(dot));
        Assert.Equal([dot], doc.Annotations);

        doc.Undo();
        Assert.Empty(doc.Annotations);
        Assert.True(doc.CanRedo);

        doc.Redo();
        Assert.Equal([dot], doc.Annotations);
    }

    [Fact(DisplayName = "execute-clears-redo")]
    public void ExecuteClearsRedo()
    {
        var doc = NewDoc();
        doc.Execute(new AddAnnotation(NewDot()));
        doc.Undo();
        doc.Execute(new AddAnnotation(NewDot()));
        Assert.False(doc.CanRedo);
    }

    [Fact(DisplayName = "remove-restores-position-on-undo")]
    public void RemoveRestoresPositionOnUndo()
    {
        var doc = NewDoc();
        Dot a = NewDot(), b = NewDot(), c = NewDot();
        doc.Execute(new AddAnnotation(a));
        doc.Execute(new AddAnnotation(b));
        doc.Execute(new AddAnnotation(c));

        doc.Execute(new RemoveAnnotation(b.Id));
        Assert.Equal([a, c], doc.Annotations);

        doc.Undo();
        Assert.Equal([a, b, c], doc.Annotations);
    }

    [Fact(DisplayName = "clear-and-undo")]
    public void ClearAndUndo()
    {
        var doc = NewDoc();
        Dot a = NewDot(), b = NewDot();
        doc.Execute(new AddAnnotation(a));
        doc.Execute(new AddAnnotation(b));

        doc.Execute(new ClearAnnotations());
        Assert.Empty(doc.Annotations);

        doc.Undo();
        Assert.Equal([a, b], doc.Annotations);
    }

    [Fact(DisplayName = "mark-saved-clears-dirty")]
    public void MarkSavedClearsDirty()
    {
        var doc = NewDoc();
        doc.MarkSaved();
        Assert.False(doc.IsDirty);

        doc.Execute(new AddAnnotation(NewDot()));
        Assert.True(doc.IsDirty);

        doc.Undo();
        Assert.False(doc.IsDirty);
    }

    [Fact(DisplayName = "undo-past-save-point-is-dirty")]
    public void UndoPastSavePointIsDirty()
    {
        var doc = NewDoc();
        doc.Execute(new AddAnnotation(NewDot()));
        doc.MarkSaved();

        doc.Undo();
        Assert.True(doc.IsDirty);

        doc.Redo();
        Assert.False(doc.IsDirty);
    }

    private static ShapeAnnotation Box(double x) =>
        new(Guid.NewGuid(), ShapeKind.Rectangle, new CaptureOk.Core.Geometry.RectD(x, 0, 5, 5), new StrokeStyle(0xFFFF0000, 3, false), false);

    [Fact(DisplayName = "replace-keeps-position-and-undo-restores")]
    public void ReplaceKeepsPositionAndUndoRestores()
    {
        var doc = NewDoc();
        ShapeAnnotation a = Box(0), b = Box(10), c = Box(20);
        foreach (var s in new[] { a, b, c }) doc.Execute(new AddAnnotation(s));

        var b2 = b with { Filled = true };
        doc.Execute(new ReplaceAnnotation(b2));
        Assert.Equal([a, b2, c], doc.Annotations);

        doc.Undo();
        Assert.Equal([a, b, c], doc.Annotations);
    }

    [Fact(DisplayName = "move-offsets-and-undo")]
    public void MoveOffsetsAndUndo()
    {
        var doc = NewDoc();
        var a = Box(0);
        doc.Execute(new AddAnnotation(a));

        doc.Execute(EditCommands.Move(doc, a.Id, 3, 4));
        Assert.Equal(new CaptureOk.Core.Geometry.RectD(3, 4, 5, 5), ((ShapeAnnotation)doc.Annotations[0]).Rect);

        doc.Undo();
        Assert.Equal(a, doc.Annotations[0]);
    }

    [Fact(DisplayName = "replace-missing-id-is-noop")]
    public void ReplaceMissingIdIsNoop()
    {
        var doc = NewDoc();
        var a = Box(0);
        doc.Execute(new AddAnnotation(a));

        doc.Execute(new ReplaceAnnotation(Box(50)));

        Assert.Equal([a], doc.Annotations);
    }

    [Fact(DisplayName = "changed-event-fires")]
    public void ChangedEventFires()
    {
        var doc = NewDoc();
        var count = 0;
        doc.Changed += () => count++;

        doc.Execute(new AddAnnotation(NewDot()));
        doc.Undo();
        doc.Redo();
        doc.MarkSaved();

        Assert.Equal(4, count);
    }

    [Fact(DisplayName = "content-version-ignores-mark-saved")]
    public void ContentVersionIgnoresMarkSaved()
    {
        var doc = NewDoc();
        var v0 = doc.ContentVersion;
        doc.Execute(new AddAnnotation(NewDot()));
        var v1 = doc.ContentVersion;
        Assert.NotEqual(v0, v1);

        doc.MarkSaved();
        Assert.Equal(v1, doc.ContentVersion); // 저장·복사는 그림을 바꾸지 않는다

        doc.Undo();
        Assert.NotEqual(v1, doc.ContentVersion);
        var v2 = doc.ContentVersion;
        doc.Redo();
        Assert.NotEqual(v2, doc.ContentVersion);
    }
}