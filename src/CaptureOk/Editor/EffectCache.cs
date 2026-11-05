using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CaptureOk.Core.Editing;
using CaptureOk.Core.Imaging;

namespace CaptureOk.Editor;

/// <summary>효과 계산 한 건: 종류, 원본에서의 정수 영역, 세기.</summary>
internal readonly record struct EffectJob(bool Blur, PixelRegion Region, int Strength);

/// <summary>
/// 원본 이미지 한 장에 대한 모자이크·블러 결과 캐시. 효과는 원본 픽셀에만 적용하고,
/// (종류, 정수 영역, 세기)가 같으면 다시 계산하지 않는다. 오래된 결과부터 버려 전체 크기를 maxBytes 안으로 유지한다.
/// 드래그 미리보기는 Prepare(UI 스레드) → Run(아무 스레드) → Finish(UI 스레드)로 나눠 화면을 멈추지 않고 계산한다.
/// </summary>
internal sealed class EffectCache(BitmapSource baseImage, long maxBytes = EffectCache.DefaultMaxBytes)
{
    public const long DefaultMaxBytes = 64L * 1024 * 1024;
    private const int MaxEntries = 64;
    private const int BlurPasses = 3;
    private const double Epsilon = 1e-6;

    private readonly Dictionary<EffectJob, BitmapSource> _cache = [];
    private readonly LinkedList<EffectJob> _order = new();
    // 다른 스레드의 Run이 비트맵에 손대지 않도록 크기는 만들 때 읽어 둔다.
    private readonly int _width = baseImage.PixelWidth;
    private readonly int _height = baseImage.PixelHeight;
    private byte[]? _pixels;
    private int _computeCount;

    /// <summary>지금까지 실제로 효과를 계산한 횟수.</summary>
    public int ComputeCount => Volatile.Read(ref _computeCount);

    /// <summary>캐시에 들고 있는 결과 이미지의 바이트 합.</summary>
    public long CachedBytes { get; private set; }

    /// <summary>효과 객체가 원본 위에 그릴 이미지와 그 영역(원본 픽셀). 효과가 아니거나 영역이 비면 null.</summary>
    public (BitmapSource Image, Rect Area)? Get(Annotation annotation)
    {
        if (Prepare(annotation) is not { } job) return null;
        if (_cache.TryGetValue(job, out var image)) return (image, AreaOf(job));
        return Finish(job, Run(job), remember: true);
    }

    /// <summary>UI 스레드: 객체가 덮을 정수 영역을 정하고 원본 픽셀을 읽어 둔다. 효과가 아니거나 영역이 비면 null.</summary>
    public EffectJob? Prepare(Annotation annotation)
    {
        var (blur, rect, strength) = annotation switch
        {
            MosaicAnnotation m => (false, m.Rect, m.BlockSize),
            BlurAnnotation b => (true, b.Rect, b.Radius),
            _ => (false, default, 0),
        };
        if (strength <= 0) return null;

        // 바깥쪽으로 맞춰서, 소수 좌표로 옮긴 뒤에도 가장자리 한 줄이 원본으로 남지 않게 한다.
        var left = (int)Math.Floor(rect.X + Epsilon);
        var top = (int)Math.Floor(rect.Y + Epsilon);
        var right = (int)Math.Ceiling(rect.Right - Epsilon);
        var bottom = (int)Math.Ceiling(rect.Bottom - Epsilon);
        var region = new PixelRegion(left, top, right - left, bottom - top).ClampTo(_width, _height);
        if (region.Width <= 0 || region.Height <= 0) return null;

        LoadPixels();
        return new EffectJob(blur, region, strength);
    }

    /// <summary>아무 스레드: 효과를 계산한 영역 픽셀(BGRA). 원본 픽셀은 읽기만 한다.</summary>
    public byte[] Run(EffectJob job)
    {
        var source = _pixels ?? throw new InvalidOperationException("Prepare를 먼저 불러야 합니다.");
        Interlocked.Increment(ref _computeCount);
        var region = job.Region;
        // 블러는 영역 바깥도 표본으로 써야 가장자리가 자연스럽다. 모자이크는 영역만.
        var margin = job.Blur ? job.Strength * BlurPasses : 0;
        var sample = new PixelRegion(region.X - margin, region.Y - margin, region.Width + margin * 2, region.Height + margin * 2).ClampTo(_width, _height);
        var buffer = new byte[sample.Width * sample.Height * 4];
        for (var y = 0; y < sample.Height; y++)
            Buffer.BlockCopy(source, ((sample.Y + y) * _width + sample.X) * 4, buffer, y * sample.Width * 4, sample.Width * 4);

        var local = new PixelRegion(region.X - sample.X, region.Y - sample.Y, region.Width, region.Height);
        if (job.Blur) PixelOps.Blur(buffer, sample.Width, sample.Height, local, job.Strength);
        else PixelOps.Pixelate(buffer, sample.Width, sample.Height, local, job.Strength);

        var result = new byte[region.Width * region.Height * 4];
        for (var y = 0; y < region.Height; y++)
            Buffer.BlockCopy(buffer, ((local.Y + y) * sample.Width + local.X) * 4, result, y * region.Width * 4, region.Width * 4);
        return result;
    }

    /// <summary>UI 스레드: 계산한 픽셀을 그릴 이미지로 만든다. remember면 캐시에 넣어 같은 영역의 Get이 다시 계산하지 않게 한다.</summary>
    public (BitmapSource Image, Rect Area) Finish(EffectJob job, byte[] pixels, bool remember)
    {
        var bitmap = BitmapSource.Create(job.Region.Width, job.Region.Height, 96, 96, PixelFormats.Bgra32, null, pixels, job.Region.Width * 4);
        bitmap.Freeze();
        if (remember && !_cache.ContainsKey(job)) Remember(job, bitmap);
        return (bitmap, AreaOf(job));
    }

    /// <summary>
    /// 옮기는 중인 효과 미리보기: 원래 자리에서 계산해 둔 결과를 (dx, dy)만큼 옮겨 그린다.
    /// 마우스를 움직일 때마다 큰 영역을 다시 계산하지 않기 위함이며, 놓으면 새 자리에서 계산한다.
    /// </summary>
    public (BitmapSource Image, Rect Area)? GetShifted(Annotation annotation, double dx, double dy)
    {
        if (Get(annotation) is not { } effect) return null;
        var area = effect.Area;
        area.Offset(dx, dy);
        return (effect.Image, area);
    }

    private void Remember(EffectJob job, BitmapSource bitmap)
    {
        _cache[job] = bitmap;
        _order.AddLast(job);
        CachedBytes += BytesOf(job.Region);
        // 방금 만든 것 하나는 한도보다 커도 남긴다.
        while (_order.Count > 1 && (_order.Count > MaxEntries || CachedBytes > maxBytes))
        {
            var oldest = _order.First!.Value;
            _order.RemoveFirst();
            _cache.Remove(oldest);
            CachedBytes -= BytesOf(oldest.Region);
        }
    }

    private static Rect AreaOf(EffectJob job) => new(job.Region.X, job.Region.Y, job.Region.Width, job.Region.Height);

    private static long BytesOf(PixelRegion r) => (long)r.Width * r.Height * 4;

    private void LoadPixels()
    {
        if (_pixels is not null) return;
        var converted = new FormatConvertedBitmap(baseImage, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[_width * _height * 4];
        converted.CopyPixels(pixels, _width * 4, 0);
        _pixels = pixels;
    }
}
