namespace CaptureOk.Core.Imaging;

/// <summary>정수 픽셀 영역.</summary>
public readonly record struct PixelRegion(int X, int Y, int Width, int Height)
{
    public PixelRegion ClampTo(int width, int height)
    {
        var left = Math.Clamp(X, 0, width);
        var top = Math.Clamp(Y, 0, height);
        var right = Math.Clamp(X + Width, 0, width);
        var bottom = Math.Clamp(Y + Height, 0, height);
        return new PixelRegion(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }
}

/// <summary>BGRA(4바이트/픽셀) 이미지의 영역 처리. 영역은 이미지 안으로 잘라서 처리한다.</summary>
public static class PixelOps
{
    private const int Channels = 4;
    private const int BlurPasses = 3; // 상자 블러 3회 ≈ 가우시안

    /// <summary>영역을 blockSize 칸으로 나눠 각 칸을 평균색으로 채운다 (결과 알파 255).</summary>
    public static void Pixelate(byte[] bgra, int width, int height, PixelRegion region, int blockSize)
    {
        var r = region.ClampTo(width, height);
        if (r.Width == 0 || r.Height == 0 || blockSize < 1) return;

        for (var by = r.Y; by < r.Y + r.Height; by += blockSize)
        for (var bx = r.X; bx < r.X + r.Width; bx += blockSize)
        {
            var bw = Math.Min(blockSize, r.X + r.Width - bx);
            var bh = Math.Min(blockSize, r.Y + r.Height - by);
            long b = 0, g = 0, red = 0;
            for (var y = by; y < by + bh; y++)
            for (var x = bx; x < bx + bw; x++)
            {
                var i = (y * width + x) * Channels;
                b += bgra[i];
                g += bgra[i + 1];
                red += bgra[i + 2];
            }
            var n = bw * bh;
            byte ab = (byte)((b + n / 2) / n), ag = (byte)((g + n / 2) / n), ar = (byte)((red + n / 2) / n);
            for (var y = by; y < by + bh; y++)
            for (var x = bx; x < bx + bw; x++)
            {
                var i = (y * width + x) * Channels;
                bgra[i] = ab;
                bgra[i + 1] = ag;
                bgra[i + 2] = ar;
                bgra[i + 3] = 255;
            }
        }
    }

    /// <summary>
    /// 영역을 흐리게 한다. 영역 바깥 픽셀도 표본으로 쓰되(가장자리가 자연스럽게) 결과는 영역 안에만 쓴다.
    /// 각 패스는 이동 창 합으로 반지름과 무관하게 픽셀 수에 비례하는 시간이 걸린다.
    /// </summary>
    public static void Blur(byte[] bgra, int width, int height, PixelRegion region, int radius)
    {
        var r = region.ClampTo(width, height);
        if (r.Width == 0 || r.Height == 0 || radius < 1) return;

        // 3번 흐리면 영향 범위가 반지름 × 3까지 퍼지므로 그만큼 여유를 두고 표본을 읽는다.
        var margin = radius * BlurPasses;
        var s = new PixelRegion(r.X - margin, r.Y - margin, r.Width + margin * 2, r.Height + margin * 2).ClampTo(width, height);
        var a = new byte[s.Width * s.Height * Channels];
        for (var y = 0; y < s.Height; y++)
            Buffer.BlockCopy(bgra, ((s.Y + y) * width + s.X) * Channels, a, y * s.Width * Channels, s.Width * Channels);
        var b = new byte[a.Length];

        for (var pass = 0; pass < BlurPasses; pass++)
        {
            BoxHorizontal(a, b, s.Width, s.Height, radius);
            BoxVertical(b, a, s.Width, s.Height, radius);
        }

        for (var y = r.Y; y < r.Y + r.Height; y++)
            Buffer.BlockCopy(a, ((y - s.Y) * s.Width + (r.X - s.X)) * Channels, bgra, (y * width + r.X) * Channels, r.Width * Channels);
    }

    private static void BoxHorizontal(byte[] src, byte[] dst, int w, int h, int radius)
    {
        var n = radius * 2 + 1;
        for (var y = 0; y < h; y++)
        {
            var row = y * w * Channels;
            for (var c = 0; c < Channels; c++)
            {
                var sum = 0;
                for (var k = -radius; k <= radius; k++) sum += src[row + Math.Clamp(k, 0, w - 1) * Channels + c];
                for (var x = 0; x < w; x++)
                {
                    dst[row + x * Channels + c] = (byte)((sum + n / 2) / n);
                    sum += src[row + Math.Min(x + radius + 1, w - 1) * Channels + c] - src[row + Math.Max(x - radius, 0) * Channels + c];
                }
            }
        }
    }

    private static void BoxVertical(byte[] src, byte[] dst, int w, int h, int radius)
    {
        var n = radius * 2 + 1;
        var stride = w * Channels;
        for (var x = 0; x < w; x++)
        {
            for (var c = 0; c < Channels; c++)
            {
                var col = x * Channels + c;
                var sum = 0;
                for (var k = -radius; k <= radius; k++) sum += src[Math.Clamp(k, 0, h - 1) * stride + col];
                for (var y = 0; y < h; y++)
                {
                    dst[y * stride + col] = (byte)((sum + n / 2) / n);
                    sum += src[Math.Min(y + radius + 1, h - 1) * stride + col] - src[Math.Max(y - radius, 0) * stride + col];
                }
            }
        }
    }
}
