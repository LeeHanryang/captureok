using System.Windows.Media;
using System.Windows.Media.Imaging;
using CaptureOk.Core.Editing;
using CaptureOk.Core.Geometry;
using CaptureOk.Editor;

namespace CaptureOk.Tests;

public class FlattenerTests
{
    /// <summary>WPF 렌더링은 STA 스레드에서만 동작한다.</summary>
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

    private static BitmapSource White(int size, double dpi = 96)
    {
        var pixels = Enumerable.Repeat((byte)0xFF, size * size * 4).ToArray();
        return BitmapSource.Create(size, size, dpi, dpi, PixelFormats.Bgra32, null, pixels, size * 4);
    }

    private static (byte B, byte G, byte R, byte A) PixelAt(BitmapSource image, int x, int y)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var buffer = new byte[4];
        converted.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), buffer, 4, 0);
        return (buffer[0], buffer[1], buffer[2], buffer[3]);
    }

    private static CaptureDocument Doc(int size) => new(Guid.NewGuid(), size, size, DateTime.Now);

    private static BitmapSource Render(CaptureDocument doc, BitmapSource image) => Flattener.Render(doc, image, new EffectCache(image));

    /// <summary>흰 바탕에 (0,0)~(redSize-1, redSize-1)만 빨강.</summary>
    private static BitmapSource WhiteWithRedCorner(int size, int redSize)
    {
        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var i = (y * size + x) * 4;
            var red = x < redSize && y < redSize;
            pixels[i] = red ? (byte)0 : (byte)255;
            pixels[i + 1] = red ? (byte)0 : (byte)255;
            pixels[i + 2] = 255;
            pixels[i + 3] = 255;
        }
        return BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
    }

    private static BitmapSource Checker(int size)
    {
        var pixels = new byte[size * size * 4];
        for (var i = 0; i < size * size; i++)
        {
            var v = ((i % size) + (i / size)) % 2 == 0 ? (byte)0 : (byte)255;
            pixels[i * 4] = pixels[i * 4 + 1] = pixels[i * 4 + 2] = v;
            pixels[i * 4 + 3] = 255;
        }
        return BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
    }

    [Fact(DisplayName = "flatten-without-annotations-equals-base")]
    public void FlattenWithoutAnnotationsEqualsBase() => RunSta(() =>
    {
        var result = Render(Doc(20), White(20));
        Assert.Equal(20, result.PixelWidth);
        Assert.Equal((0xFF, 0xFF, 0xFF, 0xFF), PixelAt(result, 10, 10));
    });

    [Fact(DisplayName = "flatten-draws-filled-rect-at-pixel-coords")]
    public void FlattenDrawsFilledRectAtPixelCoords() => RunSta(() =>
    {
        var doc = Doc(20);
        doc.Execute(new AddAnnotation(new ShapeAnnotation(Guid.NewGuid(), ShapeKind.Rectangle, new RectD(5, 5, 4, 4),
            new StrokeStyle(0xFFFF0000, 1, false), Filled: true)));

        var result = Render(doc, White(20));

        Assert.Equal((0x00, 0x00, 0xFF, 0xFF), PixelAt(result, 6, 6));
        Assert.Equal((0xFF, 0xFF, 0xFF, 0xFF), PixelAt(result, 2, 2));
    });

    [Fact(DisplayName = "flatten-ignores-base-dpi")]
    public void FlattenIgnoresBaseDpi() => RunSta(() =>
    {
        var doc = Doc(20);
        doc.Execute(new AddAnnotation(new ShapeAnnotation(Guid.NewGuid(), ShapeKind.Rectangle, new RectD(5, 5, 4, 4),
            new StrokeStyle(0xFFFF0000, 1, false), Filled: true)));

        var result = Render(doc, White(20, dpi: 192));

        Assert.Equal(20, result.PixelWidth);
        Assert.Equal(20, result.PixelHeight);
        Assert.Equal((0xFF, 0xFF, 0xFF, 0xFF), PixelAt(result, 15, 15)); // 원본이 반 크기로 그려지면 여기가 투명해진다
        Assert.Equal((0x00, 0x00, 0xFF, 0xFF), PixelAt(result, 6, 6));
    });

    [Fact(DisplayName = "flatten-crop-size-and-offset")]
    public void FlattenCropSizeAndOffset() => RunSta(() =>
    {
        var doc = Doc(20);
        doc.Execute(new SetCrop(new RectD(5, 5, 10, 10)));

        var result = Render(doc, WhiteWithRedCorner(20, 10));

        Assert.Equal(10, result.PixelWidth);
        Assert.Equal(10, result.PixelHeight);
        Assert.Equal((0x00, 0x00, 0xFF, 0xFF), PixelAt(result, 0, 0));
        Assert.Equal((0xFF, 0xFF, 0xFF, 0xFF), PixelAt(result, 9, 9));
    });

    [Fact(DisplayName = "flatten-mosaic-block-is-uniform")]
    public void FlattenMosaicBlockIsUniform() => RunSta(() =>
    {
        var doc = Doc(8);
        doc.Execute(new AddAnnotation(new MosaicAnnotation(Guid.NewGuid(), new RectD(0, 0, 8, 8), 4)));

        var result = Render(doc, Checker(8));

        var first = PixelAt(result, 0, 0);
        for (var y = 0; y < 4; y++)
        for (var x = 0; x < 4; x++)
            Assert.Equal(first, PixelAt(result, x, y));
    });

    [Fact(DisplayName = "flatten-annotation-after-crop-is-shifted")]
    public void FlattenAnnotationAfterCropIsShifted() => RunSta(() =>
    {
        var doc = Doc(20);
        doc.Execute(new SetCrop(new RectD(5, 5, 10, 10)));
        doc.Execute(new AddAnnotation(new ShapeAnnotation(Guid.NewGuid(), ShapeKind.Rectangle, new RectD(6, 6, 2, 2),
            new StrokeStyle(0xFF0000FF, 1, false), Filled: true)));

        var result = Render(doc, White(20));

        Assert.Equal((0xFF, 0x00, 0x00, 0xFF), PixelAt(result, 1, 1));
        Assert.Equal((0xFF, 0xFF, 0xFF, 0xFF), PixelAt(result, 5, 5));
    });
}