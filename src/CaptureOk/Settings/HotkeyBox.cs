using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CaptureOk.Core.Hotkeys;

namespace CaptureOk.Settings;

/// <summary>
/// 단축키 입력 칸: 누른 조합(Ctrl·Shift·Alt + 글자·숫자·F1~F12, 또는 PrintScreen)을 받아 표시한다.
/// Backspace·Delete = 지우기, Esc = 입력 취소. 수식 키 없는 글자·숫자는 글자 입력을 가로채므로 받지 않는다.
/// 칸에 초점이 있는 동안은 앱의 전역 단축키를 잠시 풀어 누른 키가 캡처로 가로채이지 않게 한다.
/// </summary>
internal sealed class HotkeyBox : TextBox
{
    private string _value = "";

    public HotkeyBox()
    {
        // 상속한 형식에는 WPF-UI의 기본 TextBox 스타일이 자동으로 붙지 않아(흰 칸) 직접 붙인다.
        SetResourceReference(StyleProperty, typeof(TextBox));
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        InputMethod.SetIsInputMethodEnabled(this, false); // 한글 입력기가 키를 바꾸지 않게
        MinWidth = 150;
        Cursor = Cursors.Hand;
        ToolTip = "눌러서 새 단축키 입력 · Backspace로 지우기";
        GotKeyboardFocus += (_, _) => { FocusChanged?.Invoke(true); Text = "키를 누르세요…"; };
        LostKeyboardFocus += (_, _) => { FocusChanged?.Invoke(false); Show(); };
    }

    /// <summary>정리된 표기 (예: "Ctrl+Shift+C"). 비었으면 "".</summary>
    public string Value
    {
        get => _value;
        set { _value = HotkeyGesture.TryParse(value, out var g) ? g.ToString() : ""; Show(); }
    }

    /// <summary>사용자가 새 키를 넣었거나 지웠다.</summary>
    public event Action<string>? Changed;

    /// <summary>초점을 얻었다(true)·잃었다(false) — 전역 단축키를 잠시 풀고 되돌리는 데 쓴다.</summary>
    public event Action<bool>? FocusChanged;

    /// <summary>안내 문구 (예: 수식 키 없이 글자를 눌렀을 때).</summary>
    public event Action<string>? Hint;

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key,
        };
        if (key == Key.Tab) return; // 다음 칸으로 이동은 그대로
        if (key == Key.F4 && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return; // Alt+F4 = 창 닫기 그대로
        e.Handled = true;
        if (key == Key.Snapshot) return; // PrintScreen은 키를 뗄 때 한 번만 받는다
        switch (key)
        {
            case Key.Escape:
                Keyboard.ClearFocus();
                return;
            case Key.Back or Key.Delete:
                Commit("");
                return;
            case Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin:
                return; // 수식 키만으로는 아직
        }
        if (Format(key, Keyboard.Modifiers) is { } text) Commit(text);
    }

    // PrintScreen은 키를 뗄 때만 오는 경우가 많다.
    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        if (e.Key is Key.Snapshot || (e.Key == Key.System && e.SystemKey == Key.Snapshot))
        {
            e.Handled = true;
            if (Format(Key.Snapshot, Keyboard.Modifiers) is { } text) Commit(text);
        }
    }

    private string? Format(Key key, ModifierKeys modifiers)
    {
        string? name = key switch
        {
            >= Key.A and <= Key.Z => key.ToString(),
            >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
            >= Key.F1 and <= Key.F12 => key.ToString(),
            Key.Snapshot => "PrintScreen",
            _ => null,
        };
        if (name is null)
        {
            Hint?.Invoke(key is >= Key.NumPad0 and <= Key.NumPad9
                ? "숫자 키패드 대신 위쪽 숫자 키를 써 주세요"
                : "글자·숫자·F1~F12·PrintScreen만 쓸 수 있습니다");
            return null;
        }
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        if (parts.Count == 0 && name.Length == 1)
        {
            Hint?.Invoke("글자·숫자는 Ctrl·Shift·Alt와 함께 눌러 주세요 (혼자서는 글자 입력을 가로챔)");
            return null;
        }
        parts.Add(name);
        return string.Join("+", parts);
    }

    /// <summary>
    /// 받은 키를 저장하고 칸에서 나간다 — 한 번 누르면 키 하나. 칸을 나가야 앱의 전역 단축키가 다시 등록되므로,
    /// 칸에 남아 있다가 다음 키가 캡처로 가로채이는 일이 없다.
    /// </summary>
    private void Commit(string text)
    {
        Value = text;
        Changed?.Invoke(Value);
        Keyboard.ClearFocus();
    }

    private void Show() => Text = _value.Length == 0 ? "(없음)" : HotkeyGesture.Display(_value);
}
