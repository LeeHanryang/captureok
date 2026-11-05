using CaptureOk.Core.Geometry;

namespace CaptureOk.Core.Editing;

/// <summary>자르기 영역에서 누른 곳: 변·모서리(조합) 또는 안쪽(이동).</summary>
[Flags]
public enum CropGrip
{
    None = 0,
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8,
    Move = 16,
}

/// <summary>자르기 영역의 손잡이 판정과 끌기 계산 (원본 이미지 픽셀 좌표).</summary>
public static class CropEditing
{
    /// <summary>변·모서리에서 tolerance 안이면 그 변(모서리는 두 변), 안쪽이면 이동, 바깥이면 None.</summary>
    public static CropGrip HitGrip(RectD crop, PointD p, double tolerance, int imageWidth, int imageHeight)
    {
        var withinX = p.X >= crop.X - tolerance && p.X <= crop.Right + tolerance;
        var withinY = p.Y >= crop.Y - tolerance && p.Y <= crop.Bottom + tolerance;
        if (!withinX || !withinY) return CropGrip.None;

        var grip = CropGrip.None;
        if (Math.Abs(p.X - crop.X) <= tolerance) grip |= CropGrip.Left;
        else if (Math.Abs(p.X - crop.Right) <= tolerance) grip |= CropGrip.Right;
        if (Math.Abs(p.Y - crop.Y) <= tolerance) grip |= CropGrip.Top;
        else if (Math.Abs(p.Y - crop.Bottom) <= tolerance) grip |= CropGrip.Bottom;
        if (grip != CropGrip.None) return grip;
        // 사진 전체가 선택된 상태에서는 옮길 곳이 없으니, 안쪽을 누르면 새로 고른다.
        var whole = crop.X <= 0 && crop.Y <= 0 && crop.Right >= imageWidth && crop.Bottom >= imageHeight;
        return crop.Contains(p) && !whole ? CropGrip.Move : CropGrip.None;
    }

    /// <summary>
    /// 누른 손잡이를 (dx, dy)만큼 끈 영역. 변은 이미지 밖으로 나가지 않고 반대 변을 넘지 않는다 (최소 1px).
    /// 이동은 크기를 유지한 채 이미지 안에 머문다.
    /// </summary>
    public static RectD Drag(RectD start, CropGrip grip, double dx, double dy, int imageWidth, int imageHeight)
    {
        if (grip.HasFlag(CropGrip.Move))
        {
            var x = Math.Clamp(start.X + dx, 0, Math.Max(0, imageWidth - start.Width));
            var y = Math.Clamp(start.Y + dy, 0, Math.Max(0, imageHeight - start.Height));
            return new RectD(x, y, start.Width, start.Height);
        }

        double left = start.X, top = start.Y, right = start.Right, bottom = start.Bottom;
        if (grip.HasFlag(CropGrip.Left)) left = Math.Clamp(left + dx, 0, right - 1);
        if (grip.HasFlag(CropGrip.Right)) right = Math.Clamp(right + dx, left + 1, imageWidth);
        if (grip.HasFlag(CropGrip.Top)) top = Math.Clamp(top + dy, 0, bottom - 1);
        if (grip.HasFlag(CropGrip.Bottom)) bottom = Math.Clamp(bottom + dy, top + 1, imageHeight);
        return new RectD(left, top, right - left, bottom - top);
    }

    /// <summary>자르기 명령이 실제로 쓸 영역: 이미지 경계로 자르고 픽셀 경계에 맞춘다. 1px 미만이면 null.</summary>
    public static RectD? Normalize(RectD rect, int imageWidth, int imageHeight)
    {
        var clamped = rect.ClampTo(imageWidth, imageHeight);
        if (clamped.Width < 1 || clamped.Height < 1) return null;
        var left = Math.Round(clamped.X);
        var top = Math.Round(clamped.Y);
        return new RectD(left, top, Math.Max(1, Math.Round(clamped.Right) - left), Math.Max(1, Math.Round(clamped.Bottom) - top));
    }
}

public static class ZoomMath
{
    /// <summary>
    /// 내용 전체가 보이는 확대율. 작은 내용은 키우지 않고(최대 1), 아직 배치 전(공간 0)이면 1.
    /// 공간보다 세 배 넘게 길쭉한 내용(스크롤 캡처)은 전체를 넣으면 읽을 수 없으므로 폭에만 맞춘다(세로 스크롤 막대 폭을 뺌).
    /// </summary>
    public static double Fit(double contentWidth, double contentHeight, double availableWidth, double availableHeight, double scrollBarWidth = 0)
    {
        if (contentWidth <= 0 || contentHeight <= 0 || availableWidth <= 0 || availableHeight <= 0) return 1;
        var whole = Math.Min(availableWidth / contentWidth, availableHeight / contentHeight);
        var width = availableWidth / contentWidth;
        if (whole < width / 3 - 1e-9) return Math.Min(1, Math.Max(1, availableWidth - scrollBarWidth) / contentWidth);
        return Math.Min(1, whole);
    }

    /// <summary>
    /// 캡처를 100%로 보이게 할 창 크기. 필요한 만큼 키우되 화면(max)보다 크게는 키우지 않고, 지금보다 줄이지는 않는다.
    /// </summary>
    public static (double Width, double Height) GrowToFit(double currentWidth, double currentHeight, double neededWidth, double neededHeight,
        double maxWidth, double maxHeight) =>
        (Math.Max(currentWidth, Math.Min(neededWidth, maxWidth)), Math.Max(currentHeight, Math.Min(neededHeight, maxHeight)));

    /// <summary>창이 영역 안에 들어오도록 시작 위치를 옮긴다. 영역보다 크면 시작에 붙인다.</summary>
    public static int ClampInto(int position, int size, int areaStart, int areaEnd) =>
        Math.Clamp(position, areaStart, Math.Max(areaStart, areaEnd - size));
}
