using System.IO;

namespace CaptureOk;

/// <summary>문제 확인용 간단한 로그 (%APPDATA%\CaptureOk\captureok.log, 1MB 넘으면 새로 시작).</summary>
internal static class AppLog
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly Lock Gate = new();

    public static string FilePath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CaptureOk", "captureok.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes) File.Delete(FilePath);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
            // 로그 실패는 앱 동작에 영향을 주지 않는다.
        }
    }
}
