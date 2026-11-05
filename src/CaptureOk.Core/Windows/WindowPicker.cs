using CaptureOk.Core.Geometry;

namespace CaptureOk.Core.Windows;

/// <summary>최상위 창 하나. Bounds는 그림자를 뺀 실제 창 경계(물리 픽셀).</summary>
public sealed record WindowInfo(nint Handle, PixelRect Bounds, string Title);

public static class WindowPicker
{
    /// <summary>z-order 앞쪽부터 검사해 점을 포함하는 첫 창. 없으면 null.</summary>
    public static WindowInfo? HitTest(IReadOnlyList<WindowInfo> zOrderTopFirst, int x, int y) =>
        zOrderTopFirst.FirstOrDefault(w => w.Bounds.Contains(x, y));
}
