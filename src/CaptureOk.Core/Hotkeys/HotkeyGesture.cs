namespace CaptureOk.Core.Hotkeys;

/// <summary>수식키 비트. 값은 Win32 RegisterHotKey의 MOD_* 와 같다.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>전역 단축키 하나. 표기 예: "Ctrl+Shift+C", "PrintScreen".</summary>
public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, int VirtualKey)
{
    private const int VkPrintScreen = 0x2C;
    private const int VkF1 = 0x70;

    private static readonly (string Name, HotkeyModifiers Flag)[] ModifierNames =
    [
        ("Ctrl", HotkeyModifiers.Control),
        ("Shift", HotkeyModifiers.Shift),
        ("Alt", HotkeyModifiers.Alt),
        ("Win", HotkeyModifiers.Win),
    ];

    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split('+').Select(p => p.Trim()).ToArray();
        var modifiers = HotkeyModifiers.None;
        int? key = null;

        foreach (var part in parts)
        {
            var modifier = ModifierNames.FirstOrDefault(m => string.Equals(m.Name, part, StringComparison.OrdinalIgnoreCase));
            if (modifier.Name is not null)
            {
                if (key is not null) return false; // 수식키는 키보다 앞에만 온다
                modifiers |= modifier.Flag;
                continue;
            }
            if (key is not null) return false; // 키는 하나만
            var vk = ParseKey(part);
            if (vk is null) return false;
            key = vk;
        }

        if (key is null) return false;
        gesture = new HotkeyGesture(modifiers, key.Value);
        return true;
    }

    /// <summary>메뉴·화면 표시용 짧은 표기 (PrintScreen → PrtSc). 읽을 수 없는 키는 빈 문자열.</summary>
    public static string Display(string? text) =>
        TryParse(text, out var gesture) ? gesture.ToString().Replace("PrintScreen", "PrtSc") : "";

    public override string ToString()
    {
        var modifiers = Modifiers;
        var names = ModifierNames.Where(m => modifiers.HasFlag(m.Flag)).Select(m => m.Name).ToList();
        names.Add(KeyName(VirtualKey));
        return string.Join("+", names);
    }

    private static int? ParseKey(string name)
    {
        if (name.Length == 1)
        {
            var c = char.ToUpperInvariant(name[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') return c;
            return null;
        }
        if (string.Equals(name, "PrintScreen", StringComparison.OrdinalIgnoreCase)) return VkPrintScreen;
        if (name.Length is 2 or 3 && (name[0] is 'F' or 'f') && int.TryParse(name.AsSpan(1), out var n) && n is >= 1 and <= 12)
            return VkF1 + n - 1;
        return null;
    }

    private static string KeyName(int vk) => vk switch
    {
        VkPrintScreen => "PrintScreen",
        >= VkF1 and <= VkF1 + 11 => $"F{vk - VkF1 + 1}",
        _ => ((char)vk).ToString(),
    };
}
