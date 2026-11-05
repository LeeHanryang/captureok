using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CaptureOk.Core.Geometry;

namespace CaptureOk.Capture;

/// <summary>커서 주변 15×15px를 8배로 보여 주는 돋보기와 좌표 표시.</summary>
internal static class Magnifier
{
    private const int SourceSize = 15;
    private const int Zoom = 8;
    private const double Offset = 20;

    public static void Draw(DrawingContext dc, BitmapSource image, PixelRect virtualBounds, int cx, int cy, double s, PixelRect monitor, Pen border, Brush labelBrush)
    {
        var half = SourceSize / 2;
        var sx = Math.Clamp(cx - half - virtualBounds.X, 0, Math.Max(0, image.PixelWidth - SourceSize));
        var sy = Math.Clamp(cy - half - virtualBounds.Y, 0, Math.Max(0, image.PixelHeight - SourceSize));
        var crop = new CroppedBitmap(image, new Int32Rect(sx, sy, SourceSize, SourceSize));

        var size = SourceSize * Zoom * s;
        var textHeight = 18 * s;
        var left = cx + Offset * s;
        var top = cy + Offset * s;
        if (left + size > monitor.Right) left = cx - Offset * s - size;
        if (top + size + textHeight > monitor.Bottom) top = cy - Offset * s - size - textHeight;

        var box = new Rect(left, top, size, size);
        dc.DrawImage(crop, box);
        dc.DrawRectangle(null, border, box);

        // 가운데 픽셀 표시
        var cell = Zoom * s;
        var centerX = left + (cx - virtualBounds.X - sx) * cell;
        var centerY = top + (cy - virtualBounds.Y - sy) * cell;
        dc.DrawRectangle(null, border, new Rect(centerX, centerY, cell, cell));

        var ft = new FormattedText($"{cx}, {cy}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11 * s, Brushes.White, 1.0);
        var label = new Rect(left, top + size, size, textHeight);
        dc.DrawRectangle(labelBrush, null, label);
        dc.DrawText(ft, new Point(left + (size - ft.Width) / 2, top + size + (textHeight - ft.Height) / 2));
    }
}
