using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CaptureOk.Core.Editing;

namespace CaptureOk.Editor;

/// <summary>
/// 캡처 목록 썸네일: 편집·자르기를 반영해 가로 width에 맞춰 줄인다 (작은 캡처는 키우지 않음).
/// 원본 크기로 합친 뒤 줄이지 않고 처음부터 썸네일 크기로 그린다 (큰 캡처 여러 장도 가볍게).
/// </summary>
internal static class Thumbnails
{
    public const int DefaultWidth = 176;

    public static BitmapSource Render(CaptureDocument doc, BitmapSource image, EffectCache effects, int width)
    {
        var view = doc.ViewRect;
        int viewWidth = Math.Max(1, doc.ViewWidth), viewHeight = Math.Max(1, doc.ViewHeight);
        var scale = Math.Min(1.0, (double)width / viewWidth);
        var w = Math.Max(1, (int)Math.Round(viewWidth * scale));
        var h = Math.Max(1, (int)Math.Round(viewHeight * scale));

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.PushTransform(new TranslateTransform(-view.X, -view.Y));
            dc.DrawImage(image, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
            foreach (var annotation in doc.Annotations) AnnotationRenderer.Draw(dc, annotation, effects);
            dc.Pop();
            dc.Pop();
        }
        var thumb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        thumb.Render(visual);
        thumb.Freeze();
        return thumb;
    }
}
