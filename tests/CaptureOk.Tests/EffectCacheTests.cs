using System.Windows.Media;
using System.Windows.Media.Imaging;
using CaptureOk.Core.Editing;
using CaptureOk.Core.Geometry;
using CaptureOk.Editor;

namespace CaptureOk.Tests;

public class EffectCacheTests
{
    /// <summary>WPF 비트맵·요소는 STA 스레드에서만 동작한다.</summary>
    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Xunit.Sdk.XunitException($"STA 스레드 오류: {error}");
    }

    private static BitmapSource Gray(int size)
    {
        var pixels = Enumerable.Repeat((byte)0x80, size * size * 4).ToArray();
        return BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
    }

    [Fact]
    public void MovingPreview_ReusesComputedEffect() => RunSta(() =>
    {
        var cache = new EffectCache(Gray(200));
        var blur = new BlurAnnotation(Guid.NewGuid(), new RectD(10, 10, 100, 100), 8);
        cache.Get(blur);

        for (var dx = 1; dx <= 30; dx++)
        {
            var preview = cache.GetShifted(blur, dx + 0.3, dx);
            Assert.NotNull(preview);
            Assert.Equal(10 + dx + 0.3, preview.Value.Area.X, 6);
            Assert.Equal(10 + dx, preview.Value.Area.Y, 6);
        }
        Assert.Equal(1, cache.ComputeCount);
    });

    [Fact]
    public void Cache_StaysUnderByteLimit() => RunSta(() =>
    {
        const long limit = 100 * 100 * 4 * 3; // 100×100 결과 세 장
        var cache = new EffectCache(Gray(300), limit);
        for (var i = 0; i < 10; i++)
        {
            var mosaic = new MosaicAnnotation(Guid.NewGuid(), new RectD(i * 10, 0, 100, 100), 12);
            Assert.NotNull(cache.Get(mosaic));
            Assert.True(cache.CachedBytes <= limit, $"{cache.CachedBytes} > {limit}");
        }
    });

    [Fact]
    public void Region_IsRoundedOutward_SoNoEdgeColumnStaysOriginal() => RunSta(() =>
    {
        var cache = new EffectCache(Gray(100));
        var mosaic = new MosaicAnnotation(Guid.NewGuid(), new RectD(11.5, 11.5, 10.5, 10.5), 8);
        var area = cache.Get(mosaic)!.Value.Area;
        Assert.Equal(11, area.X);
        Assert.Equal(11, area.Y);
        Assert.Equal(22, area.Right);
        Assert.Equal(22, area.Bottom);
    });

    [Fact]
    public void PendingCrop_FollowsUndoInCropTool() => RunSta(() =>
    {
        var doc = new CaptureDocument(Guid.NewGuid(), 200, 100, DateTime.Now);
        var canvas = new AnnotationCanvas { Document = doc };
        doc.Execute(new SetCrop(new RectD(10, 10, 50, 40)));
        canvas.Tool = EditorTool.Crop;
        Assert.Equal(new RectD(10, 10, 50, 40), canvas.PendingCrop);

        doc.Undo();
        Assert.Equal(new RectD(0, 0, 200, 100), canvas.PendingCrop); // 자르기가 없으면 사진 전체가 선택된 상태
        doc.Redo();
        Assert.Equal(new RectD(10, 10, 50, 40), canvas.PendingCrop);
    });

    [Fact]
    public void PendingCrop_StartsWithWholeImage_WhenNotCropped() => RunSta(() =>
    {
        var doc = new CaptureDocument(Guid.NewGuid(), 200, 100, DateTime.Now);
        var canvas = new AnnotationCanvas { Document = doc, Tool = EditorTool.Crop };
        Assert.Equal(new RectD(0, 0, 200, 100), canvas.PendingCrop);

        Assert.True(canvas.ApplyPendingCrop());
        Assert.False(doc.CanUndo); // 그대로 Enter는 편집이 아님
    });

    [Fact]
    public void PreviewJob_RunsOnBackgroundThread_AndMatchesGet() => RunSta(() =>
    {
        var pattern = new byte[120 * 120 * 4];
        for (var i = 0; i < pattern.Length; i++) pattern[i] = (byte)(i * 37 % 251);
        var image = BitmapSource.Create(120, 120, 96, 96, PixelFormats.Bgra32, null, pattern, 120 * 4);
        var blur = new BlurAnnotation(Guid.NewGuid(), new RectD(10, 10, 60, 40), 4);
        var preview = new EffectCache(image);
        var job = preview.Prepare(blur)!.Value;
        var pixels = Task.Run(() => preview.Run(job)).Result;
        var (image1, area1) = preview.Finish(job, pixels, remember: false);
        Assert.Equal(0, preview.CachedBytes);

        var (image2, area2) = new EffectCache(image).Get(blur)!.Value;
        Assert.Equal(area2, area1);
        Assert.Equal(Bytes(image2), Bytes(image1));
    });

    [Fact]
    public void RememberedPreview_IsReusedByGet() => RunSta(() =>
    {
        var cache = new EffectCache(Gray(120));
        var mosaic = new MosaicAnnotation(Guid.NewGuid(), new RectD(0, 0, 50, 50), 8);
        var job = cache.Prepare(mosaic)!.Value;
        cache.Finish(job, cache.Run(job), remember: true);
        var before = cache.ComputeCount;
        cache.Get(mosaic);
        Assert.Equal(before, cache.ComputeCount);
    });

    private static byte[] Bytes(BitmapSource image)
    {
        var buffer = new byte[image.PixelWidth * image.PixelHeight * 4];
        image.CopyPixels(buffer, image.PixelWidth * 4, 0);
        return buffer;
    }

    [Fact]
    public void PendingCrop_LoadsNewDocumentCrop_WhenSwitchingInCropTool() => RunSta(() =>
    {
        var first = new CaptureDocument(Guid.NewGuid(), 200, 100, DateTime.Now);
        var second = new CaptureDocument(Guid.NewGuid(), 200, 100, DateTime.Now);
        second.Execute(new SetCrop(new RectD(5, 5, 20, 30)));
        var canvas = new AnnotationCanvas { Document = first, Tool = EditorTool.Crop };

        canvas.Document = second;
        Assert.Equal(new RectD(5, 5, 20, 30), canvas.PendingCrop);
    });
}
