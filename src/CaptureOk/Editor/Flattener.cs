using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CaptureOk.Core.Editing;

namespace CaptureOk.Editor;

/// <summary>문서(원본 + 그린 객체 + 자르기)를 내보낼 이미지 한 장으로 합친다. 좌표는 원본 이미지 픽셀 1:1.</summary>
internal static class Flattener
{
    public static BitmapSource Render(CaptureDocument doc, BitmapSource baseImage, EffectCache effects)
    {
        if (doc.Annotations.Count == 0 && doc.CropRect is null) return baseImage;

        var view = doc.ViewRect;
        int width = Math.Max(1, doc.ViewWidth), height = Math.Max(1, doc.ViewHeight);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // 자른 영역의 왼쪽 위가 결과의 (0,0)이 되도록 옮긴다. 객체 좌표는 원본 기준 그대로.
            dc.PushTransform(new TranslateTransform(-view.X, -view.Y));
            // 원본의 DPI 정보와 상관없이 픽셀 크기 그대로 깐다.
            dc.DrawImage(baseImage, new Rect(0, 0, baseImage.PixelWidth, baseImage.PixelHeight));
            foreach (var annotation in doc.Annotations) AnnotationRenderer.Draw(dc, annotation, effects);
            dc.Pop();
        }
        var result = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        result.Render(visual);
        result.Freeze();
        return result;
    }
}
