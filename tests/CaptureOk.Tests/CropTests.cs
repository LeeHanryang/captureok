using CaptureOk.Core.Editing;
using CaptureOk.Core.Geometry;

namespace CaptureOk.Tests;

public class CropTests
{
    private static CaptureDocument Doc(int w, int h) => new(Guid.NewGuid(), w, h, DateTime.Now);

    [Fact(DisplayName = "mosaic-bounds-and-offset")]
    public void MosaicBoundsAndOffset()
    {
        var m = new MosaicAnnotation(Guid.NewGuid(), new RectD(1, 2, 30, 40), 12);
        Assert.Equal(new RectD(1, 2, 30, 40), m.Bounds);
        Assert.Equal(new RectD(11, 22, 30, 40), ((MosaicAnnotation)m.Offset(10, 20)).Rect);
    }

    [Fact(DisplayName = "mosaic-inside-hits")]
    public void MosaicInsideHits()
    {
        var m = new MosaicAnnotation(Guid.NewGuid(), new RectD(0, 0, 50, 50), 12);
        Assert.Same(m, HitTester.Hit([m], new PointD(25, 25), 4));
        Assert.Null(HitTester.Hit([m], new PointD(80, 80), 4));
    }

    [Fact(DisplayName = "blur-inside-hits")]
    public void BlurInsideHits()
    {
        var b = new BlurAnnotation(Guid.NewGuid(), new RectD(0, 0, 50, 50), 8);
        Assert.Same(b, HitTester.Hit([b], new PointD(25, 25), 4));
    }

    [Fact(DisplayName = "crop-sets-view-and-undo-restores")]
    public void CropSetsViewAndUndoRestores()
    {
        var doc = Doc(100, 50);
        Assert.Equal(new RectD(0, 0, 100, 50), doc.ViewRect);

        doc.Execute(new SetCrop(new RectD(10, 10, 30, 20)));
        Assert.Equal(30, doc.ViewWidth);
        Assert.Equal(20, doc.ViewHeight);
        Assert.Equal(new RectD(10, 10, 30, 20), doc.CropRect);

        doc.Undo();
        Assert.Null(doc.CropRect);
        Assert.Equal(100, doc.ViewWidth);
        Assert.Equal(50, doc.ViewHeight);
    }

    [Fact(DisplayName = "crop-is-clamped")]
    public void CropIsClamped()
    {
        var doc = Doc(40, 40);
        doc.Execute(new SetCrop(new RectD(-10, -10, 50, 50)));
        Assert.Equal(new RectD(0, 0, 40, 40), doc.CropRect);
    }

    [Fact(DisplayName = "crop-redo-after-other-edit")]
    public void CropRedoAfterOtherEdit()
    {
        var doc = Doc(100, 50);
        doc.Execute(new SetCrop(new RectD(10, 10, 30, 20)));
        doc.Execute(new AddAnnotation(new MosaicAnnotation(Guid.NewGuid(), new RectD(12, 12, 5, 5), 8)));

        doc.Undo();
        doc.Undo();
        Assert.Null(doc.CropRect);
        Assert.Empty(doc.Annotations);

        doc.Redo();
        doc.Redo();
        Assert.Equal(30, doc.ViewWidth);
        Assert.Single(doc.Annotations);
    }

    [Fact(DisplayName = "crop-makes-dirty")]
    public void CropMakesDirty()
    {
        var doc = Doc(100, 50);
        doc.MarkSaved();
        doc.Execute(new SetCrop(new RectD(10, 10, 30, 20)));
        Assert.True(doc.IsDirty);
    }
}
