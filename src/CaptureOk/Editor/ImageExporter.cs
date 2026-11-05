using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace CaptureOk.Editor;

internal static class ImageExporter
{
    private const int ClipboardRetries = 5;
    private const int ClipboardRetryDelayMs = 100;

    /// <summary>
    /// PNG로 저장한다. 폴더가 없으면 만든다. 실패하면 IOException·UnauthorizedAccessException이 그대로 올라간다.
    /// overwrite가 true이면 기존 파일을 지우지 않고 그 자리에 덮어쓴다 (열기에 실패하면 기존 파일은 그대로 남는다).
    /// </summary>
    public static void SavePng(BitmapSource image, string path, bool overwrite = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new FileStream(path, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write);
        encoder.Save(stream);
    }

    /// <summary>다른 프로그램이 클립보드를 잠그고 있으면 잠깐씩 기다리며 다시 시도한다.</summary>
    public static bool TryCopyToClipboard(BitmapSource image)
    {
        for (var attempt = 0; attempt < ClipboardRetries; attempt++)
        {
            try
            {
                Clipboard.SetImage(image);
                return true;
            }
            catch (COMException)
            {
                Thread.Sleep(ClipboardRetryDelayMs);
            }
        }
        return false;
    }
}
