using System.IO;
using System.Windows.Media.Imaging;
using CaptureOk.Core.Editing;
using CaptureOk.Editor;

namespace CaptureOk.Tests;

public class ToolCursorsTests
{
    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Exception(error.Message, error);
    }

    private static byte Alpha(BitmapSource image, int x, int y)
    {
        var pixel = new byte[4];
        image.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return pixel[3];
    }

    [Theory(DisplayName = "tool-cursor-tip-is-the-hotspot")]
    [InlineData(ToolCursor.Pen, 32)]
    [InlineData(ToolCursor.Highlighter, 32)]
    [InlineData(ToolCursor.Eraser, 32)]
    [InlineData(ToolCursor.Pen, 48)]
    public void ToolCursorTipIsTheHotspot(ToolCursor kind, int size) => RunSta(() =>
    {
        var built = ToolCursors.Build(kind, size);

        Assert.Equal(size, built.Image.PixelWidth);
        Assert.Equal(size, built.Image.PixelHeight);
        // 그리는 점(핫스폿)은 도구 끝 — 그림이 있는 픽셀이고, 왼쪽 아래 쪽에 있다
        Assert.True(Alpha(built.Image, built.HotX, built.HotY) > 0, $"핫스폿 ({built.HotX},{built.HotY})에 그림이 없음");
        Assert.True(built.HotX < size / 2 && built.HotY > size / 2, $"핫스폿 ({built.HotX},{built.HotY})가 왼쪽 아래가 아님");
        // 밝은 바탕·어두운 바탕 모두에서 보이도록 흰 몸통과 검은 테두리가 함께 있다
        var pixels = new byte[size * size * 4];
        built.Image.CopyPixels(pixels, size * 4, 0);
        bool white = false, black = false;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i + 3] < 250) continue;
            if (pixels[i] > 230 && pixels[i + 1] > 230 && pixels[i + 2] > 230) white = true;
            if (pixels[i] < 25 && pixels[i + 1] < 25 && pixels[i + 2] < 25) black = true;
        }
        Assert.True(white && black);
        // 아이콘 글꼴을 못 읽으면 대신 네모(빈 글자 상자)가 그려진다 — 펜·지우개는 기울어진 모양이라 위·아래 줄의 그림 폭이 같지 않다
        Assert.False(LooksLikeEmptyBox(pixels, size), "아이콘 대신 빈 네모가 그려짐");
    });

    /// <summary>그림이 있는 픽셀들의 경계 상자 테두리가 거의 다 차 있고 가운데가 비어 있으면 빈 글자 상자.</summary>
    private static bool LooksLikeEmptyBox(byte[] pixels, int size)
    {
        bool On(int x, int y) => pixels[(y * size + x) * 4 + 3] > 128;
        int minX = size, minY = size, maxX = -1, maxY = -1;
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                if (On(x, y)) { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
        if (maxX < 0) return true;
        int edge = 0, edgeOn = 0;
        for (var x = minX; x <= maxX; x++) { edge += 2; edgeOn += (On(x, minY) ? 1 : 0) + (On(x, maxY) ? 1 : 0); }
        for (var y = minY; y <= maxY; y++) { edge += 2; edgeOn += (On(minX, y) ? 1 : 0) + (On(maxX, y) ? 1 : 0); }
        return edgeOn > edge * 0.9;
    }

    [Theory(DisplayName = "tool-cursor-is-about-as-big-as-system-cursors")]
    [InlineData(ToolCursor.Pen, 32)]
    [InlineData(ToolCursor.Highlighter, 32)]
    [InlineData(ToolCursor.Eraser, 32)]
    [InlineData(ToolCursor.Pen, 48)]
    [InlineData(ToolCursor.Eraser, 64)]
    public void ToolCursorIsAboutAsBigAsSystemCursors(ToolCursor kind, int size) => RunSta(() =>
    {
        // 기본 커서는 32칸에서 화살표 12×19, 손 17×23 — 칸을 꽉 채우면 기본 커서의 두 배 가까이 커 보인다
        var image = ToolCursors.Build(kind, size).Image;
        var pixels = new byte[size * size * 4];
        image.CopyPixels(pixels, size * 4, 0);
        int minX = size, minY = size, maxX = -1, maxY = -1;
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                if (pixels[(y * size + x) * 4 + 3] > 0) { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }

        var longest = Math.Max(maxX - minX + 1, maxY - minY + 1);
        Assert.InRange(longest, (int)(size * 0.55), (int)(size * 0.72));
    });

    [Fact(DisplayName = "tool-cursor-file-is-a-valid-cursor")]
    public void ToolCursorFileIsAValidCursor() => RunSta(() =>
    {
        var built = ToolCursors.Build(ToolCursor.Pen, 32);
        var cur = built.CursorFile;

        Assert.Equal(2, BitConverter.ToUInt16(cur, 2)); // 2 = 커서 파일
        Assert.Equal(1, BitConverter.ToUInt16(cur, 4));
        Assert.Equal(built.HotX, BitConverter.ToUInt16(cur, 6 + 4));
        Assert.Equal(built.HotY, BitConverter.ToUInt16(cur, 6 + 6));
        using var cursor = new System.Windows.Input.Cursor(new MemoryStream(cur));
    });
}
