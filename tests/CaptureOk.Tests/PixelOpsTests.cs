using System.Diagnostics;
using CaptureOk.Core.Imaging;

namespace CaptureOk.Tests;

public class PixelOpsTests
{
    /// <summary>회색조 값 배열로 BGRA 이미지를 만든다 (알파 255).</summary>
    private static byte[] Gray(params byte[] values)
    {
        var bgra = new byte[values.Length * 4];
        for (var i = 0; i < values.Length; i++)
        {
            bgra[i * 4] = bgra[i * 4 + 1] = bgra[i * 4 + 2] = values[i];
            bgra[i * 4 + 3] = 255;
        }
        return bgra;
    }

    private static byte[] Checker(int size) =>
        Gray(Enumerable.Range(0, size * size).Select(i => (byte)(((i % size) + (i / size)) % 2 == 0 ? 0 : 255)).ToArray());

    private static byte GrayAt(byte[] bgra, int width, int x, int y) => bgra[(y * width + x) * 4];

    [Fact(DisplayName = "pixelate-block-is-average")]
    public void PixelateBlockIsAverage()
    {
        var img = Checker(4);
        PixelOps.Pixelate(img, 4, 4, new PixelRegion(0, 0, 4, 4), 2);
        for (var y = 0; y < 4; y++)
        for (var x = 0; x < 4; x++)
            Assert.InRange(GrayAt(img, 4, x, y), 127, 128);
    }

    [Fact(DisplayName = "pixelate-leaves-outside")]
    public void PixelateLeavesOutside()
    {
        var img = Checker(8);
        var before = GrayAt(img, 8, 5, 5);
        PixelOps.Pixelate(img, 8, 8, new PixelRegion(0, 0, 4, 4), 4);
        Assert.Equal(before, GrayAt(img, 8, 5, 5));
    }

    [Fact(DisplayName = "pixelate-partial-last-block")]
    public void PixelatePartialLastBlock()
    {
        var img = Gray(0, 0, 0, 0, 255);
        PixelOps.Pixelate(img, 5, 1, new PixelRegion(0, 0, 5, 1), 4);
        Assert.Equal([0, 0, 0, 0, 255], Enumerable.Range(0, 5).Select(x => GrayAt(img, 5, x, 0)));
    }

    [Fact(DisplayName = "pixelate-region-outside-image-is-clamped")]
    public void PixelateRegionOutsideImageIsClamped()
    {
        var img = Checker(4);
        var before = GrayAt(img, 4, 3, 3);
        PixelOps.Pixelate(img, 4, 4, new PixelRegion(-2, -2, 4, 4), 2);
        Assert.InRange(GrayAt(img, 4, 0, 0), 127, 128);
        Assert.Equal(before, GrayAt(img, 4, 3, 3));
    }

    [Fact(DisplayName = "blur-uniform-stays-uniform")]
    public void BlurUniformStaysUniform()
    {
        var img = Gray(Enumerable.Repeat((byte)90, 64).ToArray());
        PixelOps.Blur(img, 8, 8, new PixelRegion(0, 0, 8, 8), 3);
        Assert.All(Enumerable.Range(0, 64), i => Assert.Equal(90, img[i * 4]));
    }

    [Fact(DisplayName = "blur-softens-edge")]
    public void BlurSoftensEdge()
    {
        var img = Gray(Enumerable.Range(0, 20).Select(x => (byte)(x < 10 ? 0 : 255)).ToArray());
        PixelOps.Blur(img, 20, 1, new PixelRegion(0, 0, 20, 1), 2);
        Assert.InRange(GrayAt(img, 20, 9, 0), 1, 254);
        Assert.InRange(GrayAt(img, 20, 10, 0), 1, 254);
    }

    [Fact(DisplayName = "blur-leaves-outside")]
    public void BlurLeavesOutside()
    {
        var img = Gray(Enumerable.Range(0, 20).Select(x => (byte)(x < 10 ? 0 : 255)).ToArray());
        PixelOps.Blur(img, 20, 1, new PixelRegion(5, 0, 10, 1), 2);
        Assert.Equal(0, GrayAt(img, 20, 0, 0));
        Assert.Equal(255, GrayAt(img, 20, 19, 0));
    }

    [Fact(DisplayName = "blur-large-region-is-fast")]
    public void BlurLargeRegionIsFast()
    {
        const int w = 2560, h = 1440;
        var img = new byte[w * h * 4];
        new Random(1).NextBytes(img);
        var clock = Stopwatch.StartNew();
        PixelOps.Blur(img, w, h, new PixelRegion(0, 0, w, h), 16);
        Assert.True(clock.ElapsedMilliseconds < 2000, $"{clock.ElapsedMilliseconds}ms");
    }
}
