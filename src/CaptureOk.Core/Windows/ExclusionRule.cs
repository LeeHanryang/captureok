using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CaptureOk.Core.Windows;

/// <summary>
/// 자동 감지·캡처에서 뺄 창 규칙. 적힌 조건(프로세스 이름·창 클래스·제목 정규식)이 모두 맞으면 뺀다.
/// 프로세스 이름은 확장자·대소문자를, 창 클래스는 대소문자를 무시한다. 잘못된 정규식은 그 조건만 무시한다.
/// 조건이 하나도 없으면 아무것도 빼지 않는다.
/// </summary>
public sealed record ExclusionRule(string? Process, string? WindowClass, string? TitlePattern)
{
    private Regex? _regex;
    private bool _regexReady;

    public bool Matches(string process, string windowClass, string title)
    {
        var any = false;
        if (!string.IsNullOrWhiteSpace(Process))
        {
            any = true;
            if (!string.Equals(Strip(Process), Strip(process), StringComparison.OrdinalIgnoreCase)) return false;
        }
        if (!string.IsNullOrWhiteSpace(WindowClass))
        {
            any = true;
            if (!string.Equals(WindowClass, windowClass, StringComparison.OrdinalIgnoreCase)) return false;
        }
        if (Pattern() is { } regex)
        {
            any = true;
            try
            {
                if (!regex.IsMatch(title)) return false;
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }
        return any;
    }

    /// <summary>정규식은 한 번만 만든다 (창마다 다시 만들지 않게). 잘못된 식이면 null.</summary>
    private Regex? Pattern()
    {
        if (_regexReady) return _regex;
        _regexReady = true;
        if (string.IsNullOrWhiteSpace(TitlePattern)) return null;
        try
        {
            _regex = new Regex(TitlePattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
        }
        catch (ArgumentException)
        {
            _regex = null; // 잘못된 정규식: 이 조건은 없는 것으로 본다
        }
        return _regex;
    }

    private static string Strip(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
}
