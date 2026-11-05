using System.Windows.Media;
using System.Windows.Media.Imaging;
using CaptureOk.Capture;
using CaptureOk.Core.Geometry;
using CaptureOk.Native;

namespace CaptureOk.Tests;

public class CursorCompositeTests
{
    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Xunit.Sdk.XunitException($"STA 스레드 오류: {error}");
    }

    private static BitmapSource White(int size)
    {
        var pixels = Enumerable.Repeat((byte)0xFF, size * size * 4).ToArray();
        return BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
    }

    private static (int Dark, int Opaque) Count(BitmapSource image)
    {
        var pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
        new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, image.PixelWidth * 4, 0);
        int dark = 0, opaque = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i] < 100 && pixels[i + 1] < 100 && pixels[i + 2] < 100) dark++;
            if (pixels[i + 3] == 255) opaque++;
        }
        return (dark, opaque);
    }

    [Theory(DisplayName = "cursor-is-drawn-onto-capture")]
    [InlineData(32512)] // 화살표
    [InlineData(32513)] // 글자 커서 (아래 색을 뒤집는 흑백 커서)
    public void CursorIsDrawnOntoCapture(int systemCursor) => RunSta(() =>
    {
        var shared = NativeMethods.LoadCursor(0, systemCursor);
        Assert.NotEqual(0, shared);
        using var cursor = new CursorShot(NativeMethods.CopyIcon(shared), 110, 120); // 화면 좌표 (영역은 100,100부터)
        var image = White(64);

        var result = ScreenGrabber.WithCursor(image, new PixelRect(100, 100, 64, 64), cursor);

        var (dark, opaque) = Count(result);
        Assert.True(dark > 5, $"커서가 그려지지 않음 (어두운 픽셀 {dark})");
        Assert.Equal(64 * 64, opaque); // 결과는 화면처럼 불투명
    });

    [Fact(DisplayName = "cursor-outside-region-leaves-image")]
    public void CursorOutsideRegionLeavesImage() => RunSta(() =>
    {
        using var cursor = new CursorShot(NativeMethods.CopyIcon(NativeMethods.LoadCursor(0, 32512)), 900, 900);
        var image = White(32);
        Assert.Same(image, ScreenGrabber.WithCursor(image, new PixelRect(0, 0, 32, 32), cursor));
    });
}
