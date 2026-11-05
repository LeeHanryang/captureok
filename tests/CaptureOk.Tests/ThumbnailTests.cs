using System.Windows.Media;
using System.Windows.Media.Imaging;
using CaptureOk.Core.Editing;
using CaptureOk.Core.Geometry;
using CaptureOk.Editor;

namespace CaptureOk.Tests;

public class ThumbnailTests
{
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

    private static BitmapSource Gray(int width, int height)
    {
        var pixels = Enumerable.Repeat((byte)0x80, width * height * 4).ToArray();
        return BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
    }

    private static (int, int) SizeOf(CaptureDocument doc, BitmapSource image)
    {
        var thumb = Thumbnails.Render(doc, image, new EffectCache(image), 176);
        Assert.True(thumb.IsFrozen);
        return (thumb.PixelWidth, thumb.PixelHeight);
    }

    [Fact(DisplayName = "thumbnail-keeps-aspect-and-crop")]
    public void ThumbnailKeepsAspectAndCrop() => RunSta(() =>
    {
        var wide = Gray(400, 200);
        var doc = new CaptureDocument(Guid.NewGuid(), 400, 200, DateTime.Now);
        Assert.Equal((176, 88), SizeOf(doc, wide));

        doc.Execute(new SetCrop(new RectD(0, 0, 200, 200)));
        Assert.Equal((176, 176), SizeOf(doc, wide));

        var small = Gray(100, 50);
        Assert.Equal((100, 50), SizeOf(new CaptureDocument(Guid.NewGuid(), 100, 50, DateTime.Now), small)); // 키우지 않음
    });
}
