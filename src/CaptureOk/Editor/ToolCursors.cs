using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CaptureOk.Core.Editing;
using CaptureOk.Native;
using Wpf.Ui.Controls;

namespace CaptureOk.Editor;

/// <summary>
/// 편집 캔버스의 도구 모양 마우스 커서 (펜·형광펜·지우개).
/// 도구 막대와 같은 아이콘 글꼴(Fluent UI System Icons, 채운 모양)을 흰 몸통·검은 테두리로 그려 밝은·어두운 캡처 위에서 모두 보이게 하고,
/// 그리는 점(핫스폿)은 도구 끝(그림의 왼쪽 아래 끝)에 둔다. 만들지 못하면 십자 커서.
/// </summary>
internal static class ToolCursors
{
    /// <summary>테두리까지 포함한 그림의 긴 변이 커서 칸에서 차지하는 비율.</summary>
    private const double GlyphShare = 0.65;

    private static readonly Dictionary<ToolCursor, Cursor> Made = [];

    public sealed record Built(BitmapSource Image, int HotX, int HotY, byte[] CursorFile);

    public static Cursor For(ToolCursor kind) => kind switch
    {
        ToolCursor.Arrow => Cursors.Arrow,
        ToolCursor.Text => Cursors.IBeam,
        ToolCursor.Cross => Cursors.Cross,
        _ => Cached(kind),
    };

    private static Cursor Cached(ToolCursor kind)
    {
        if (Made.TryGetValue(kind, out var cursor)) return cursor;
        try
        {
            // 시스템 커서 크기에 맞춘다 (화면 배율·접근성 "포인터 크기"를 따라감)
            var size = Math.Clamp(NativeMethods.GetSystemMetrics(NativeMethods.SM_CXCURSOR), 32, 256);
            cursor = new Cursor(new MemoryStream(Build(kind, size).CursorFile));
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException
            or System.Runtime.InteropServices.COMException or UriFormatException)
        {
            AppLog.Write($"도구 커서 만들기 실패 {kind}: {ex.Message}");
            cursor = Cursors.Cross;
        }
        Made[kind] = cursor;
        return cursor;
    }

    /// <summary>size×size 커서 그림과 핫스폿, .cur 파일 내용.</summary>
    public static Built Build(ToolCursor kind, int size)
    {
        var symbol = kind switch
        {
            ToolCursor.Pen => SymbolFilled.Pen24,
            ToolCursor.Highlighter => SymbolFilled.Highlight24,
            ToolCursor.Eraser => SymbolFilled.Eraser24,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "그림 커서가 없는 종류"),
        };
        var image = Render(symbol, size);
        var (hotX, hotY) = TipOf(image);
        return new Built(image, hotX, hotY, CursorFile(image, hotX, hotY));
    }

    private static BitmapSource Render(SymbolFilled symbol, int size)
    {
        // 글꼴 이름으로 찾으면 못 찾을 때 빈 네모가 그려지므로, 글꼴 파일에서 그 글자의 윤곽을 직접 꺼낸다
        var font = new GlyphTypeface(new Uri(FilledFontFile()));
        if (!font.CharacterToGlyphMap.TryGetValue((int)symbol, out var index))
            throw new InvalidOperationException($"아이콘 글꼴에 {symbol}이 없습니다");
        var glyph = font.GetGlyphOutline(index, size, size);
        var bounds = glyph.Bounds;
        if (bounds.IsEmpty) throw new InvalidOperationException($"아이콘 {symbol}의 윤곽이 비어 있습니다");

        // 커서 칸은 기본 커서 그림보다 훨씬 크다 (32칸에 화살표 12×19). 칸을 채우지 않고 기본 커서와 비슷한 크기로 그리고,
        // 도구 끝이 칸의 왼쪽 아래 구석에 오도록 붙인다
        var outline = Math.Max(1.0, size / 24.0);
        var scale = (size * GlyphShare - outline * 2) / Math.Max(bounds.Width, bounds.Height);
        var shape = glyph.Clone();
        shape.Transform = new TransformGroup
        {
            Children =
            {
                new TranslateTransform(-bounds.X, -bounds.Y),
                new ScaleTransform(scale, scale),
                new TranslateTransform(outline, size - outline - 1 - bounds.Height * scale),
            },
        };

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawGeometry(null, new Pen(Brushes.Black, outline * 2) { LineJoin = PenLineJoin.Round }, shape);
            dc.DrawGeometry(Brushes.White, null, shape);
        }
        var target = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        var straight = new FormatConvertedBitmap(target, PixelFormats.Bgra32, null, 0);
        straight.Freeze();
        return straight;
    }

    private static string? _fontFile;

    /// <summary>
    /// WPF UI 라이브러리에 들어 있는 아이콘 글꼴(채운 모양)을 임시 폴더에 한 번 꺼내 둔 파일 경로.
    /// (GlyphTypeface는 파일에서만 읽을 수 있다. 라이브러리 버전마다 다른 이름이라 업데이트되면 새로 꺼낸다.)
    /// </summary>
    private static string FilledFontFile()
    {
        if (_fontFile is not null) return _fontFile;
        const string resourceName = "resources/fonts/fluentsystemicons-filled.ttf";
        var library = typeof(SymbolIcon).Assembly;
        var path = Path.Combine(Path.GetTempPath(), "CaptureOk", $"FluentSystemIcons-Filled-{library.GetName().Version}.ttf");
        if (!File.Exists(path))
        {
            using var resources = library.GetManifestResourceStream($"{library.GetName().Name}.g.resources")
                ?? throw new InvalidOperationException("아이콘 글꼴 자원을 찾지 못했습니다");
            using var reader = new System.Resources.ResourceReader(resources);
            var entry = reader.Cast<System.Collections.DictionaryEntry>().FirstOrDefault(e => (string)e.Key == resourceName);
            if (entry.Value is not Stream font) throw new InvalidOperationException("아이콘 글꼴 자원을 찾지 못했습니다");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var partial = path + ".tmp";
            using (var file = File.Create(partial)) font.CopyTo(file);
            File.Move(partial, path, overwrite: true);
        }
        return _fontFile = path;
    }

    /// <summary>그림이 있는 픽셀 가운데 가장 왼쪽 아래 끝(대각선 방향) — 펜촉·형광펜 끝·지우개 모서리.</summary>
    private static (int X, int Y) TipOf(BitmapSource image)
    {
        int w = image.PixelWidth, h = image.PixelHeight;
        var pixels = new byte[w * h * 4];
        image.CopyPixels(pixels, w * 4, 0);
        int bestX = 0, bestY = h - 1, best = int.MinValue;
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                if (pixels[(y * w + x) * 4 + 3] < 128) continue;
                var score = y - x;
                if (score > best || (score == best && y > bestY))
                {
                    best = score;
                    bestX = x;
                    bestY = y;
                }
            }
        return (bestX, bestY);
    }

    /// <summary>.cur 파일: 머리(종류 2 = 커서) + 항목(핫스폿 포함) + 32비트 DIB(아래에서 위로 BGRA + 1비트 AND 마스크).</summary>
    private static byte[] CursorFile(BitmapSource image, int hotX, int hotY)
    {
        int w = image.PixelWidth, h = image.PixelHeight;
        var pixels = new byte[w * h * 4];
        image.CopyPixels(pixels, w * 4, 0);
        var maskRow = (w + 31) / 32 * 4;
        var mask = new byte[maskRow * h];
        using var dib = new MemoryStream();
        using (var d = new BinaryWriter(dib, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            d.Write(40); d.Write(w); d.Write(h * 2); d.Write((short)1); d.Write((short)32); d.Write(0);
            d.Write(pixels.Length + mask.Length); d.Write(0); d.Write(0); d.Write(0); d.Write(0);
            for (var y = h - 1; y >= 0; y--)
            {
                d.Write(pixels, y * w * 4, w * 4);
                for (var x = 0; x < w; x++)
                    if (pixels[(y * w + x) * 4 + 3] == 0) mask[(h - 1 - y) * maskRow + x / 8] |= (byte)(0x80 >> (x % 8)); // 투명한 곳
            }
            d.Write(mask);
        }
        var body = dib.ToArray();
        using var file = new MemoryStream();
        using (var f = new BinaryWriter(file))
        {
            f.Write((short)0); f.Write((short)2); f.Write((short)1);
            f.Write((byte)(w >= 256 ? 0 : w)); f.Write((byte)(h >= 256 ? 0 : h)); f.Write((byte)0); f.Write((byte)0);
            f.Write((short)hotX); f.Write((short)hotY);
            f.Write(body.Length); f.Write(6 + 16);
            f.Write(body);
        }
        return file.ToArray();
    }
}
