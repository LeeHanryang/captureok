using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using CaptureOk.Core.Geometry;
using CaptureOk.Native;

namespace CaptureOk.Capture;

/// <summary>단축키 순간의 가상 화면 전체를 한 장으로 정지 캡처한다 (GDI).</summary>
internal static class ScreenGrabber
{
    private const int MDT_EFFECTIVE_DPI = 0;

    public static ScreenLayout CurrentLayout()
    {
        var monitors = new List<MonitorInfo>();
        NativeMethods.EnumDisplayMonitors(0, 0, (hMonitor, _, ref _, _) =>
        {
            var info = new NativeMethods.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            if (NativeMethods.GetMonitorInfoW(hMonitor, ref info))
            {
                var r = info.rcMonitor;
                var scale = NativeMethods.GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out var dpi, out _) == 0 ? dpi / 96.0 : 1.0;
                monitors.Add(new MonitorInfo(new PixelRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top), scale,
                    (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0));
            }
            return true;
        }, 0);
        return new ScreenLayout(monitors);
    }

    public static (BitmapSource Image, ScreenLayout Layout) GrabVirtualScreen()
    {
        var layout = CurrentLayout();
        var vb = layout.VirtualBounds;

        var screenDc = NativeMethods.GetDC(0);
        var memDc = NativeMethods.CreateCompatibleDC(screenDc);
        var bitmap = NativeMethods.CreateCompatibleBitmap(screenDc, vb.Width, vb.Height);
        var old = NativeMethods.SelectObject(memDc, bitmap);
        try
        {
            NativeMethods.BitBlt(memDc, 0, 0, vb.Width, vb.Height, screenDc, vb.X, vb.Y, NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT);
            NativeMethods.SelectObject(memDc, old);
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return (source, layout);
        }
        finally
        {
            NativeMethods.DeleteObject(bitmap);
            NativeMethods.DeleteDC(memDc);
            NativeMethods.ReleaseDC(0, screenDc);
        }
    }

    /// <summary>
    /// 찍는 순간의 마우스 커서 모양과 그 왼쪽 위 위치(가상 화면 좌표, 핫스팟 보정). 커서가 숨겨져 있으면 null.
    /// 정지 화면에는 넣지 않고(영역을 고를 때 커서가 두 개로 보이므로) 캡처 결과에만 합친다.
    /// </summary>
    public static CursorShot? GrabCursor()
    {
        try
        {
            var info = new NativeMethods.CURSORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.CURSORINFO>() };
            if (!NativeMethods.GetCursorInfo(ref info) || (info.flags & NativeMethods.CURSOR_SHOWING) == 0 || info.hCursor == 0) return null;
            int hotX = 0, hotY = 0;
            if (NativeMethods.GetIconInfo(info.hCursor, out var icon))
            {
                (hotX, hotY) = (icon.xHotspot, icon.yHotspot);
                if (icon.hbmMask != 0) NativeMethods.DeleteObject(icon.hbmMask);
                if (icon.hbmColor != 0) NativeMethods.DeleteObject(icon.hbmColor);
            }
            // 캡처를 마칠 때까지 커서 모양이 바뀌어도 그 순간 모양을 쓰도록 복사해 둔다
            var copy = NativeMethods.CopyIcon(info.hCursor);
            return copy == 0 ? null : new CursorShot(copy, info.ptScreenPos.X - hotX, info.ptScreenPos.Y - hotY);
        }
        catch (Exception ex)
        {
            AppLog.Write($"커서 모양을 읽지 못함: {ex.Message}"); // 커서는 덤 — 캡처는 계속
            return null;
        }
    }

    /// <summary>
    /// 캡처 결과(rect 영역)에 커서를 Windows가 그리는 그대로(DrawIconEx) 합친다 — 글자 커서처럼 아래 색을 뒤집는 커서도 맞게.
    /// 커서가 영역 밖이거나 실패하면 원래 이미지를 돌려준다.
    /// </summary>
    public static BitmapSource WithCursor(BitmapSource image, PixelRect rect, CursorShot cursor)
    {
        int w = image.PixelWidth, h = image.PixelHeight;
        var x = cursor.X - rect.X;
        var y = cursor.Y - rect.Y;
        if (x >= w || y >= h || x + 64 <= 0 || y + 64 <= 0) return image;
        nint dc = 0, dib = 0, old = 0;
        try
        {
            var pixels = new byte[w * h * 4];
            new FormatConvertedBitmap(image, System.Windows.Media.PixelFormats.Bgra32, null, 0).CopyPixels(pixels, w * 4, 0);
            var header = new NativeMethods.BITMAPINFOHEADER
            {
                biSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                biWidth = w,
                biHeight = -h, // 위에서 아래로
                biPlanes = 1,
                biBitCount = 32,
            };
            dc = NativeMethods.CreateCompatibleDC(0);
            dib = NativeMethods.CreateDIBSection(dc, ref header, 0, out var bits, 0, 0);
            if (dib == 0 || bits == 0) return image;
            old = NativeMethods.SelectObject(dc, dib);
            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bits, pixels.Length);
            NativeMethods.DrawIconEx(dc, x, y, cursor.Handle, 0, 0, 0, 0, NativeMethods.DI_NORMAL);
            NativeMethods.GdiFlush();
            System.Runtime.InteropServices.Marshal.Copy(bits, pixels, 0, pixels.Length);
            for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255; // GDI는 알파를 건드리므로 화면처럼 불투명하게
            var result = BitmapSource.Create(w, h, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, w * 4);
            result.Freeze();
            return result;
        }
        catch (Exception ex)
        {
            AppLog.Write($"커서를 합치지 못함: {ex.Message}");
            return image;
        }
        finally
        {
            if (old != 0) NativeMethods.SelectObject(dc, old);
            if (dib != 0) NativeMethods.DeleteObject(dib);
            if (dc != 0) NativeMethods.DeleteDC(dc);
        }
    }

    /// <summary>지금 화면의 rect 영역(가상 화면 좌표)을 BGRA 픽셀(위에서 아래로)로 찍는다 — 스크롤 캡처·GIF 녹화의 한 장.</summary>
    /// <param name="withCursor">마우스 커서도 Windows가 그리는 그대로 넣는다 (GIF 녹화)</param>
    /// <param name="buffer">여기에 찍는다 (GIF 녹화처럼 계속 찍을 때 큰 배열을 매번 만들지 않게). 없으면 새로 만든다</param>
    public static byte[] GrabRegionPixels(PixelRect rect, bool withCursor = false, byte[]? buffer = null)
    {
        nint screenDc = 0, dc = 0, dib = 0, old = 0;
        try
        {
            screenDc = NativeMethods.GetDC(0);
            dc = NativeMethods.CreateCompatibleDC(screenDc);
            var header = new NativeMethods.BITMAPINFOHEADER
            {
                biSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                biWidth = rect.Width,
                biHeight = -rect.Height,
                biPlanes = 1,
                biBitCount = 32,
            };
            dib = NativeMethods.CreateDIBSection(dc, ref header, 0, out var bits, 0, 0);
            if (dib == 0 || bits == 0) throw new InvalidOperationException("화면을 찍을 비트맵을 만들지 못했습니다");
            old = NativeMethods.SelectObject(dc, dib);
            // 잠금 화면·사용자 계정 컨트롤 화면일 때는 실패한다 — 검은 화면을 이어 붙이지 않게 알린다
            if (!NativeMethods.BitBlt(dc, 0, 0, rect.Width, rect.Height, screenDc, rect.X, rect.Y, NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT))
                throw new InvalidOperationException("화면을 찍지 못했습니다");
            if (withCursor)
            {
                using var cursor = GrabCursor();
                if (cursor is not null) NativeMethods.DrawIconEx(dc, cursor.X - rect.X, cursor.Y - rect.Y, cursor.Handle, 0, 0, 0, 0, NativeMethods.DI_NORMAL);
            }
            NativeMethods.GdiFlush();
            var pixels = buffer ?? new byte[rect.Width * rect.Height * 4];
            System.Runtime.InteropServices.Marshal.Copy(bits, pixels, 0, pixels.Length);
            for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            return pixels;
        }
        finally
        {
            if (old != 0) NativeMethods.SelectObject(dc, old);
            if (dib != 0) NativeMethods.DeleteObject(dib);
            if (dc != 0) NativeMethods.DeleteDC(dc);
            if (screenDc != 0) NativeMethods.ReleaseDC(0, screenDc);
        }
    }

    /// <summary>정지 이미지에서 가상 화면 좌표 영역을 복사해 독립된 이미지로 만든다 (원본 큰 이미지를 붙잡지 않도록).</summary>
    public static BitmapSource Crop(BitmapSource full, ScreenLayout layout, PixelRect rect)
    {
        var vb = layout.VirtualBounds;
        var region = new Int32Rect(rect.X - vb.X, rect.Y - vb.Y, rect.Width, rect.Height);
        var stride = rect.Width * 4;
        var pixels = new byte[stride * rect.Height];
        new FormatConvertedBitmap(full, System.Windows.Media.PixelFormats.Bgra32, null, 0).CopyPixels(region, pixels, stride, 0);
        var result = BitmapSource.Create(rect.Width, rect.Height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }
}
