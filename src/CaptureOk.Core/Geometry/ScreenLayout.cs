namespace CaptureOk.Core.Geometry;

/// <summary>캡처 순간의 모니터 배치. 모든 좌표는 물리 픽셀 가상 화면 기준.</summary>
public sealed class ScreenLayout
{
    public ScreenLayout(IReadOnlyList<MonitorInfo> monitors)
    {
        if (monitors.Count == 0) throw new ArgumentException("모니터가 하나 이상 있어야 합니다.", nameof(monitors));
        Monitors = monitors;
        var left = monitors.Min(m => m.Bounds.X);
        var top = monitors.Min(m => m.Bounds.Y);
        var right = monitors.Max(m => m.Bounds.Right);
        var bottom = monitors.Max(m => m.Bounds.Bottom);
        VirtualBounds = new PixelRect(left, top, right - left, bottom - top);
    }

    public IReadOnlyList<MonitorInfo> Monitors { get; }
    public PixelRect VirtualBounds { get; }
    public MonitorInfo Primary => Monitors.FirstOrDefault(m => m.IsPrimary) ?? Monitors[0];

    /// <summary>점이 들어 있는 모니터. 어느 모니터에도 없으면 가장 가까운 모니터.</summary>
    public MonitorInfo MonitorAt(int x, int y) =>
        Monitors.FirstOrDefault(m => m.Bounds.Contains(x, y)) ?? Monitors.MinBy(m => DistanceSquared(m.Bounds, x, y))!;

    public PixelRect Clamp(PixelRect rect) => rect.Intersect(VirtualBounds);

    public (double X, double Y) ToLocalDip(MonitorInfo monitor, int x, int y) =>
        ((x - monitor.Bounds.X) / monitor.Scale, (y - monitor.Bounds.Y) / monitor.Scale);

    private static long DistanceSquared(PixelRect r, int x, int y)
    {
        long dx = x < r.X ? r.X - x : x >= r.Right ? x - r.Right + 1 : 0;
        long dy = y < r.Y ? r.Y - y : y >= r.Bottom ? y - r.Bottom + 1 : 0;
        return dx * dx + dy * dy;
    }
}
