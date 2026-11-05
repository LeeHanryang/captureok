using CaptureOk.Core.Geometry;

namespace CaptureOk.Core.Imaging;

/// <summary>스크롤 캡처에서 한 번에 굴릴 휠 양, 커서를 둘 곳, 진행 표시를 둘 곳을 정한다.</summary>
public static class ScrollPacing
{
    /// <summary>휠 한 칸.</summary>
    public const int Notch = 120;
    /// <summary>가장 작게 굴리는 양 (1/3칸). 낮은 영역에서 한 칸이 너무 많이 움직일 때.</summary>
    public const int MinDelta = 40;
    public const int MaxDelta = Notch * 10;

    /// <summary>
    /// 지난번에 delta만큼 굴려 lastShift줄 움직였을 때, 다음에는 구간의 절반쯤 움직이도록 굴릴 양.
    /// 한 칸 이상이면 칸 단위로(칸 단위로만 움직이는 프로그램이 많다), 겹침이 넉넉하도록 내림.
    /// </summary>
    public static int NextDelta(int lastShift, int delta, int band)
    {
        if (lastShift <= 0 || delta <= 0) return Math.Min(Math.Max(delta, MinDelta) * 2, MaxDelta);
        var want = band * 0.5 / ((double)lastShift / delta);
        var step = want >= Notch ? Notch : MinDelta;
        return Math.Clamp((int)(want / step) * step, MinDelta, MaxDelta);
    }

    /// <summary>겹치는 곳을 못 찾았을 때(너무 많이 움직임) 다시 해 볼 양: 절반, 적어도 1/3칸.</summary>
    public static int Backoff(int delta)
    {
        var half = delta / 2;
        var step = half >= Notch ? Notch : MinDelta;
        return Math.Max(MinDelta, half / step * step);
    }

    /// <summary>
    /// 휠을 굴린 뒤 커서를 둘 곳: 영역 밖 오른쪽(대개 스크롤 막대나 창 밖), 안 되면 왼쪽·위·아래.
    /// 커서 아래 줄을 강조하는 프로그램의 강조가 영역에 찍히지 않게.
    /// </summary>
    public static (int X, int Y) ParkPoint(PixelRect region, PixelRect monitor)
    {
        const int gap = 8;
        var cy = region.Y + region.Height / 2;
        var cx = region.X + region.Width / 2;
        if (region.Right + gap < monitor.Right) return (region.Right + gap, cy);
        if (region.X - gap >= monitor.X) return (region.X - gap, cy);
        if (region.Y - gap >= monitor.Y) return (cx, region.Y - gap);
        return (cx, Math.Min(region.Bottom + gap, monitor.Bottom - 1));
    }

    /// <summary>
    /// 진행 표시 창(물리 픽셀 width×height)의 자리: 모니터 위 가운데, 영역과 겹치면 아래 가운데.
    /// 둘 다 겹치면 위에 두되, 캡처에서 숨길 수 없으면(오래된 Windows) 보이지 않게 한다.
    /// </summary>
    public static (int X, int Y, bool Visible) IndicatorPlace(PixelRect region, PixelRect monitor, int width, int height, double scale, bool hiddenFromCapture)
    {
        var x = monitor.X + (monitor.Width - width) / 2;
        var top = monitor.Y + (int)Math.Round(12 * scale);
        var bottom = monitor.Bottom - height - (int)Math.Round(56 * scale); // 작업 표시줄 위
        if (new PixelRect(x, top, width, height).Intersect(region).IsEmpty) return (x, top, true);
        if (new PixelRect(x, bottom, width, height).Intersect(region).IsEmpty) return (x, bottom, true);
        return (x, top, hiddenFromCapture);
    }
}
