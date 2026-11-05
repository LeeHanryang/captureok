using CaptureOk.Core.Editing;
using CaptureOk.Core.Geometry;

namespace CaptureOk.Tests;

/// <summary>M2-3 보완: 드래그 지우개, 자르기 손잡이, 창에 맞춤.</summary>
public class EditorFollowupTests
{
    private static readonly StrokeStyle Red3 = new(0xFFFF0000, 3, Dashed: false);

    private static CaptureDocument Doc(int w = 200, int h = 100) => new(Guid.NewGuid(), w, h, DateTime.Now);

    private static ShapeAnnotation Box(double x, double y) =>
        new(Guid.NewGuid(), ShapeKind.Rectangle, new RectD(x, y, 10, 10), Red3, Filled: true);

    // ---- 지우개 ----

    [Fact(DisplayName = "remove-annotations-is-one-undo-step")]
    public void RemoveAnnotationsIsOneUndoStep()
    {
        var doc = Doc();
        var (a, b, c, d) = (Box(0, 0), Box(20, 0), Box(40, 0), Box(60, 0));
        foreach (var x in new[] { a, b, c, d }) doc.Execute(new AddAnnotation(x));

        doc.Execute(new RemoveAnnotations([d.Id, b.Id]));
        Assert.Equal([a, c], doc.Annotations);

        doc.Undo();
        Assert.Equal([a, b, c, d], doc.Annotations); // 원래 순서(겹침 순서) 그대로
        doc.Redo();
        Assert.Equal([a, c], doc.Annotations);
    }

    [Fact(DisplayName = "hit-along-finds-objects-between-fast-mouse-samples")]
    public void HitAlongFindsObjectsBetweenSamples()
    {
        var left = Box(20, 0);
        var right = Box(120, 0);
        var missed = Box(60, 50);
        var hits = HitTester.HitAlong([left, right, missed], new PointD(0, 5), new PointD(190, 5), 4, new HashSet<Guid>());
        Assert.Equal([left.Id, right.Id], hits.Select(h => h.Id)); // 지나간 순서대로
    }

    [Fact(DisplayName = "hit-along-skips-already-erased")]
    public void HitAlongSkipsAlreadyErased()
    {
        var top = Box(20, 0);
        var below = Box(20, 0);
        var hits = HitTester.HitAlong([below, top], new PointD(25, 5), new PointD(25, 5), 4, new HashSet<Guid> { top.Id });
        Assert.Equal([below], hits);
    }

    [Fact(DisplayName = "eraser-click-removes-only-the-top-object")]
    public void EraserClickRemovesOnlyTop()
    {
        var (bottom, middle, top) = (Box(20, 0), Box(20, 0), Box(20, 0));
        var hits = HitTester.HitAlong([bottom, middle, top], new PointD(25, 5), new PointD(25, 5), 4, new HashSet<Guid>());
        Assert.Equal([top], hits);
    }

    [Fact(DisplayName = "hit-along-continuing-segment-does-not-resample-start")]
    public void HitAlongContinuingSegmentDoesNotResampleStart()
    {
        // 이전 이동에서 이미 표본을 잡은 시작점은 다시 보지 않는다 (같은 점에서 겹친 것이 줄줄이 지워지지 않게)
        var (below, top) = (Box(20, 0), Box(20, 0));
        var hits = HitTester.HitAlong([below, top], new PointD(25, 5), new PointD(26, 5), 4, new HashSet<Guid>());
        Assert.Equal([top], hits);
    }

    [Fact(DisplayName = "hit-along-respects-filter")]
    public void HitAlongRespectsFilter()
    {
        var mosaic = new MosaicAnnotation(Guid.NewGuid(), new RectD(0, 0, 100, 100), 12);
        var pen = Box(40, 40);
        var hits = HitTester.HitAlong([mosaic, pen], new PointD(10, 45), new PointD(90, 45), 4, new HashSet<Guid>(),
            canHit: a => a is not MosaicAnnotation);
        Assert.Equal([pen], hits);
    }

    // ---- 자르기 손잡이 ----

    [Fact(DisplayName = "crop-press-inside-whole-image-starts-new-selection")]
    public void CropPressInsideWholeImageStartsNewSelection()
    {
        var whole = new RectD(0, 0, 200, 100);
        Assert.Equal(CropGrip.None, CropEditing.HitGrip(whole, new PointD(60, 35), 4, 200, 100));
        Assert.Equal(CropGrip.Left, CropEditing.HitGrip(whole, new PointD(1, 35), 4, 200, 100)); // 변은 그대로 끌 수 있음
    }

    private static readonly RectD Crop = new(10, 10, 100, 50);

    [Theory(DisplayName = "crop-grip-hit")]
    [InlineData(10, 10, CropGrip.Left | CropGrip.Top)]
    [InlineData(110, 60, CropGrip.Right | CropGrip.Bottom)]
    [InlineData(110, 35, CropGrip.Right)]
    [InlineData(60, 61, CropGrip.Bottom)]
    [InlineData(8, 35, CropGrip.Left)]
    [InlineData(60, 35, CropGrip.Move)]
    [InlineData(150, 35, CropGrip.None)]
    [InlineData(60, 80, CropGrip.None)]
    public void CropGripHit(double x, double y, CropGrip expected) =>
        Assert.Equal(expected, CropEditing.HitGrip(Crop, new PointD(x, y), 4, 200, 100));

    [Fact(DisplayName = "crop-drag-left-edge-shrinks")]
    public void CropDragLeftEdgeShrinks() =>
        Assert.Equal(new RectD(30, 10, 80, 50), CropEditing.Drag(Crop, CropGrip.Left, 20, 99, 200, 100));

    [Fact(DisplayName = "crop-drag-edge-stops-at-one-pixel")]
    public void CropDragEdgeStopsAtOnePixel() =>
        Assert.Equal(new RectD(109, 10, 1, 50), CropEditing.Drag(Crop, CropGrip.Left, 500, 0, 200, 100));

    [Fact(DisplayName = "crop-drag-edge-stops-at-image-border")]
    public void CropDragEdgeStopsAtImageBorder() =>
        Assert.Equal(new RectD(10, 0, 190, 60), CropEditing.Drag(Crop, CropGrip.Right | CropGrip.Top, 500, -500, 200, 100));

    [Fact(DisplayName = "crop-move-keeps-size-inside-image")]
    public void CropMoveKeepsSizeInsideImage() =>
        Assert.Equal(new RectD(0, 50, 100, 50), CropEditing.Drag(Crop, CropGrip.Move, -50, 300, 200, 100));

    [Fact(DisplayName = "crop-normalize-snaps-like-set-crop")]
    public void CropNormalizeSnapsLikeSetCrop()
    {
        var doc = Doc();
        var raw = new RectD(10.4, 0.6, 20.4, 5.2);
        doc.Execute(new SetCrop(raw));
        Assert.Equal(doc.CropRect, CropEditing.Normalize(raw, 200, 100));
        Assert.Equal(new RectD(10, 1, 21, 5), CropEditing.Normalize(raw, 200, 100));
        Assert.Null(CropEditing.Normalize(new RectD(5, 5, 0.3, 10), 200, 100));
    }

    // ---- 창에 맞춤 ----

    [Theory(DisplayName = "fit-zoom")]
    [InlineData(2000, 1000, 1000, 800, 0.5)]   // 가로가 넘침
    [InlineData(1000, 2000, 1000, 800, 0.4)]   // 세로가 넘침
    [InlineData(300, 200, 1000, 800, 1.0)]     // 작은 캡처는 키우지 않음
    [InlineData(300, 200, 0, 0, 1.0)]          // 아직 배치 전이면 100%
    public void FitZoom(double w, double h, double availW, double availH, double expected) =>
        Assert.Equal(expected, ZoomMath.Fit(w, h, availW, availH), 6);

    [Theory(DisplayName = "fit-zoom-tall-capture-fits-width")]
    [InlineData(1200, 9000, 1000, 800, 17, 983.0 / 1200)] // 스크롤 캡처: 전체를 넣으면 9%라 못 읽음 → 폭에 맞추고 세로로 스크롤 (세로 막대만큼 뺌)
    [InlineData(600, 9000, 1000, 800, 17, 1.0)]           // 폭이 좁으면 100%
    [InlineData(1000, 2400, 1000, 800, 17, 1.0 / 3)]      // 화면 세 배 높이까지는 전체가 보이게
    public void FitZoomTallCaptureFitsWidth(double w, double h, double availW, double availH, double scrollBar, double expected) =>
        Assert.Equal(expected, ZoomMath.Fit(w, h, availW, availH, scrollBar), 6);

    [Theory(DisplayName = "grow-window-for-full-size")]
    [InlineData(1240, 820, 1500, 900, 1900, 1000, 1500, 900)]   // 화면 안에 들어가면 100%로 보이는 크기까지 키움
    [InlineData(1240, 820, 2600, 1500, 1900, 1000, 1900, 1000)] // 화면보다 크면 화면 크기까지만 (그 뒤는 줄여서 보기)
    [InlineData(1240, 820, 600, 400, 1900, 1000, 1240, 820)]    // 작은 캡처는 창을 줄이지 않음
    [InlineData(1240, 820, 1500, 600, 1900, 1000, 1500, 820)]   // 가로·세로를 따로 계산
    [InlineData(2000, 1100, 2600, 1500, 1900, 1000, 2000, 1100)] // 사용자가 이미 화면보다 크게 늘려 둔 창은 그대로
    public void GrowWindowForFullSize(double curW, double curH, double needW, double needH, double maxW, double maxH, double expW, double expH) =>
        Assert.Equal((expW, expH), ZoomMath.GrowToFit(curW, curH, needW, needH, maxW, maxH));

    [Theory(DisplayName = "keep-window-inside-work-area")]
    [InlineData(100, 500, 0, 1920, 100)]    // 들어가면 그대로
    [InlineData(1600, 500, 0, 1920, 1420)]  // 오른쪽으로 넘치면 안으로
    [InlineData(-50, 500, 0, 1920, 0)]      // 왼쪽으로 넘치면 안으로
    [InlineData(100, 2500, 0, 1920, 0)]     // 영역보다 크면 왼쪽에 붙임
    public void KeepWindowInsideWorkArea(int x, int width, int areaStart, int areaEnd, int expected) =>
        Assert.Equal(expected, ZoomMath.ClampInto(x, width, areaStart, areaEnd));
}
