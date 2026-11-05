using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CaptureOk.Core.Imaging;

namespace CaptureOk.Tests;

/// <summary>결과는 우리 코드가 아니라 Windows의 GIF 해독기(WIC)로 풀어 픽셀까지 비교한다.</summary>
public class GifEncoderTests
{
    private const uint Red = 0xFFFF0000, Blue = 0xFF0000FF, White = 0xFFFFFFFF, Black = 0xFF000000;

    private static byte[] Solid(int w, int h, uint argb)
    {
        var pixels = new byte[w * h * 4];
        for (var i = 0; i < w * h; i++) Set(pixels, w, i % w, i / w, argb);
        return pixels;
    }

    private static void Set(byte[] pixels, int w, int x, int y, uint argb) =>
        BitConverter.TryWriteBytes(pixels.AsSpan((y * w + x) * 4), argb);

    private static uint Get(byte[] pixels, int w, int x, int y) =>
        BitConverter.ToUInt32(pixels, (y * w + x) * 4);

    private static byte[] Encode(int w, int h, params (byte[] Pixels, long Time)[] frames)
    {
        using var stream = new MemoryStream();
        var gif = new GifEncoder(stream, w, h);
        foreach (var (pixels, time) in frames) gif.Add(pixels, time);
        gif.Finish(frames[^1].Time + 100);
        return stream.ToArray();
    }

    private sealed record Decoded(byte[] Canvas, int Left, int Top, int Width, int Height, int DelayCs);

    /// <summary>프레임마다 화면에 쌓아 그 시점에 보이는 그림과 프레임 정보를 돌려준다.</summary>
    private static List<Decoded> Decode(byte[] file, int w, int h)
    {
        var decoder = new GifBitmapDecoder(new MemoryStream(file), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var canvas = new byte[w * h * 4];
        var result = new List<Decoded>();
        foreach (var frame in decoder.Frames)
        {
            var meta = (BitmapMetadata)frame.Metadata;
            int left = (ushort)meta.GetQuery("/imgdesc/Left"), top = (ushort)meta.GetQuery("/imgdesc/Top");
            var delay = (ushort)meta.GetQuery("/grctlext/Delay");
            int fw = frame.PixelWidth, fh = frame.PixelHeight;
            var pixels = new byte[fw * fh * 4];
            new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, fw * 4, 0);
            for (var y = 0; y < fh; y++)
            {
                for (var x = 0; x < fw; x++)
                {
                    var color = Get(pixels, fw, x, y);
                    if (color >> 24 != 0) Set(canvas, w, left + x, top + y, color); // 투명 = 앞 프레임 그대로
                }
            }
            result.Add(new Decoded((byte[])canvas.Clone(), left, top, fw, fh, delay));
        }
        return result;
    }

    [Fact(DisplayName = "gif-format: GIF89a, 크기, 무한 반복, 끝 표시")]
    public void FileFormat()
    {
        var file = Encode(300, 2, (Solid(300, 2, Red), 0));

        Assert.Equal("GIF89a", Encoding.ASCII.GetString(file, 0, 6));
        Assert.Equal(300, BitConverter.ToUInt16(file, 6));
        Assert.Equal(2, BitConverter.ToUInt16(file, 8));
        Assert.Contains("NETSCAPE2.0", Encoding.ASCII.GetString(file));
        Assert.Equal(0x3B, file[^1]);
    }

    [Fact(DisplayName = "gif-exact: 색이 적은 화면은 픽셀까지 그대로")]
    public void FewColorsExact()
    {
        const int w = 40, h = 30;
        var pixels = Solid(w, h, White);
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                if ((x + y) % 7 == 0) Set(pixels, w, x, y, Red);
                else if (x % 5 == 0) Set(pixels, w, x, y, Blue);
                else if (y % 9 == 0) Set(pixels, w, x, y, Black);
            }
        }

        var frames = Decode(Encode(w, h, (pixels, 0)), w, h);

        Assert.Single(frames);
        Assert.Equal(pixels, frames[0].Canvas);
    }

    [Fact(DisplayName = "gif-diff: 둘째 프레임은 바뀐 칸만 쓰고, 쌓은 화면은 원본과 같다")]
    public void SecondFrameIsChangedBoxOnly()
    {
        const int w = 100, h = 80;
        var first = Solid(w, h, White);
        var second = (byte[])first.Clone();
        Set(second, w, 20, 10, Red);
        Set(second, w, 35, 50, Blue);

        var frames = Decode(Encode(w, h, (first, 0), (second, 100)), w, h);

        Assert.Equal(2, frames.Count);
        Assert.Equal((20, 10, 16, 41), (frames[1].Left, frames[1].Top, frames[1].Width, frames[1].Height));
        Assert.Equal(second, frames[1].Canvas);
    }

    [Fact(DisplayName = "gif-same: 같은 화면은 프레임을 늘리지 않고 앞 프레임을 더 오래 보인다")]
    public void IdenticalFramesMerge()
    {
        const int w = 10, h = 10;
        var a = Solid(w, h, White);
        var b = Solid(w, h, Red);

        var frames = Decode(Encode(w, h, (a, 0), (a, 70), (a, 140), (b, 300)), w, h);

        Assert.Equal(2, frames.Count);
        Assert.Equal(30, frames[0].DelayCs);
        Assert.Equal(10, frames[1].DelayCs); // Finish가 100ms 뒤
    }

    [Fact(DisplayName = "gif-alpha: 알파만 다른 화면은 같은 화면이다")]
    public void AlphaIsIgnored()
    {
        const int w = 4, h = 4;
        var frames = Decode(Encode(w, h, (Solid(w, h, 0xFFFF0000), 0), (Solid(w, h, 0x00FF0000), 100)), w, h);

        Assert.Single(frames);
    }

    [Fact(DisplayName = "gif-delay: 지연 시간을 반올림해도 전체 길이가 밀리지 않는다")]
    public void DelayRoundingDoesNotDrift()
    {
        const int w = 2, h = 2;
        var inputs = new List<(byte[], long)>();
        for (var i = 0; i < 300; i++) inputs.Add((Solid(w, h, i % 2 == 0 ? White : Black), i * 1000L / 15));

        var frames = Decode(Encode(w, h, inputs.ToArray()), w, h);

        Assert.Equal(300, frames.Count);
        Assert.Equal(1993 + 10, frames.Sum(f => f.DelayCs)); // 299/15초 = 19.93초 + Finish까지 0.1초
        Assert.All(frames, f => Assert.InRange(f.DelayCs, 6, 10));
    }

    [Fact(DisplayName = "gif-min-delay: 너무 짧은 간격도 해독기가 무시하지 않을 최소 지연을 쓴다")]
    public void MinimumDelay()
    {
        const int w = 2, h = 2;
        var frames = Decode(Encode(w, h, (Solid(w, h, White), 0), (Solid(w, h, Black), 1), (Solid(w, h, White), 2)), w, h);

        Assert.All(frames.SkipLast(1), f => Assert.Equal(2, f.DelayCs));
    }

    [Fact(DisplayName = "gif-many-colors: 색이 256개를 넘어도 원본에 가깝게")]
    public void ManyColorsAreClose()
    {
        const int w = 256, h = 256;
        var pixels = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++) Set(pixels, w, x, y, 0xFF000000u | (uint)x << 16 | (uint)y << 8 | (uint)((x + y) / 2));
        }

        var canvas = Decode(Encode(w, h, (pixels, 0)), w, h)[0].Canvas;

        long error = 0;
        for (var i = 0; i < pixels.Length; i++) error += Math.Abs(pixels[i] - canvas[i]);
        Assert.InRange(error / (double)(w * h * 3), 0, 12); // 채널당 평균 오차
    }

    [Fact(DisplayName = "gif-lzw: 부호 표가 가득 차는 큰 화면도 그대로 풀린다")]
    public void LzwTableOverflow()
    {
        const int w = 900, h = 700;
        var random = new Random(1234);
        var colors = new uint[] { Red, Blue, White, Black, 0xFF00FF00, 0xFFFFFF00, 0xFF00FFFF, 0xFFFF00FF };
        var pixels = Solid(w, h, White);
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++) Set(pixels, w, x, y, colors[random.Next(colors.Length)]);
        }

        Assert.Equal(pixels, Decode(Encode(w, h, (pixels, 0)), w, h)[0].Canvas);
    }

    [Theory(DisplayName = "gif-sizes: 작은 크기와 색 수의 경계")]
    [InlineData(1, 1, 1)]
    [InlineData(2, 1, 2)]
    [InlineData(5, 1, 3)]
    [InlineData(3, 3, 5)]
    [InlineData(16, 16, 128)]
    [InlineData(16, 16, 255)]
    [InlineData(16, 16, 256)]
    public void SmallSizesAndColorCounts(int w, int h, int colorCount)
    {
        // 채널당 4 간격의 색 — 서로 가까워도 그대로 남아야 한다
        var pixels = new byte[w * h * 4];
        for (var i = 0; i < w * h; i++)
        {
            var c = (uint)(i % colorCount);
            Set(pixels, w, i % w, i / w, 0xFF000000u | (c & 63) * 4 << 8 | (c >> 6) * 4);
        }

        Assert.Equal(pixels, Decode(Encode(w, h, (pixels, 0)), w, h)[0].Canvas);
    }

    [Fact(DisplayName = "gif-small-change: 둘째 프레임에 색이 하나만 바뀌어도 풀린다")]
    public void SingleColorChange()
    {
        const int w = 8, h = 8;
        var first = Solid(w, h, White);
        var second = (byte[])first.Clone();
        Set(second, w, 3, 3, Red);

        Assert.Equal(second, Decode(Encode(w, h, (first, 0), (second, 100)), w, h)[1].Canvas);
    }

    [Fact(DisplayName = "gif-no-flicker: 안 바뀐 칸은 프레임마다 색이 달라지지 않는다")]
    public void UnchangedPixelsKeepTheirColor()
    {
        const int w = 64, h = 64;
        var random = new Random(7);
        var first = new byte[w * h * 4];
        for (var i = 0; i < w * h; i++) Set(first, w, i % w, i / w, 0xFF000000 | (uint)random.Next(0x1000000));
        var second = (byte[])first.Clone();
        for (var x = 0; x < w; x++) Set(second, w, x, 30, Red); // 한 줄만 바뀜

        var frames = Decode(Encode(w, h, (first, 0), (second, 100)), w, h);

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                if (y != 30) Assert.Equal(Get(frames[0].Canvas, w, x, y), Get(frames[1].Canvas, w, x, y));
            }
        }
    }

    [Fact(DisplayName = "gif-size-limit: GIF에 담을 수 없는 크기는 거부")]
    public void RejectsBadSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GifEncoder(new MemoryStream(), 0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GifEncoder(new MemoryStream(), 65536, 10));
    }

    [Fact(DisplayName = "gif-bad-buffer: 크기가 맞지 않는 화면은 거부")]
    public void RejectsWrongBufferSize()
    {
        var gif = new GifEncoder(new MemoryStream(), 4, 4);
        Assert.Throws<ArgumentException>(() => gif.Add(new byte[10], 0));
    }
}
