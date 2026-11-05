using System.Text;

namespace CaptureOk.Core.Files;

/// <summary>저장 파일 이름 규칙. 템플릿은 strftime 형식 토큰(%Y %m %d %H %M %S)을 쓴다.</summary>
public static class FileNamer
{
    // 치환되지 않고 남은 '%'도 파일 이름에서 '_'로 바꾼다.
    private static readonly HashSet<char> InvalidChars = [.. Path.GetInvalidFileNameChars(), '%'];

    public static string Next(string folder, string template, DateTime now, string extension, Func<string, bool> exists)
    {
        var stem = Sanitize(Expand(template, now));
        if (stem.Length == 0) stem = now.ToString("yyyyMMdd_HHmmss");

        var candidate = Path.Combine(folder, stem + extension);
        for (var n = 1; exists(candidate); n++)
            candidate = Path.Combine(folder, $"{stem}_{n}{extension}");
        return candidate;
    }

    private static string Expand(string template, DateTime now)
    {
        var sb = new StringBuilder(template.Length + 8);
        for (var i = 0; i < template.Length; i++)
        {
            if (template[i] == '%' && i + 1 < template.Length)
            {
                var value = template[i + 1] switch
                {
                    'Y' => now.ToString("yyyy"),
                    'm' => now.ToString("MM"),
                    'd' => now.ToString("dd"),
                    'H' => now.ToString("HH"),
                    'M' => now.ToString("mm"),
                    'S' => now.ToString("ss"),
                    _ => null,
                };
                if (value is not null)
                {
                    sb.Append(value);
                    i++;
                    continue;
                }
            }
            sb.Append(template[i]);
        }
        return sb.ToString();
    }

    private static string Sanitize(string name) =>
        new string(name.Select(c => InvalidChars.Contains(c) ? '_' : c).ToArray()).Trim();
}
