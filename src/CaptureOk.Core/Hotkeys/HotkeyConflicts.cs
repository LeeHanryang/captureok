using CaptureOk.Core.Settings;

namespace CaptureOk.Core.Hotkeys;

public static class HotkeyConflicts
{
    /// <summary>
    /// 둘 이상의 캡처 방식이 같은 키를 쓰는 곳. 표기가 달라도(대소문자·공백) 같은 키면 겹친 것으로 본다.
    /// 결과: 방식 → 그 방식의 겹치는 키(정리된 표기).
    /// </summary>
    public static IReadOnlyDictionary<CaptureMode, IReadOnlyList<string>> Duplicates(IReadOnlyDictionary<CaptureMode, List<string>> map)
    {
        var users = new Dictionary<HotkeyGesture, HashSet<CaptureMode>>();
        foreach (var (mode, keys) in map)
        {
            foreach (var key in keys ?? [])
            {
                if (!HotkeyGesture.TryParse(key, out var gesture)) continue;
                if (!users.TryGetValue(gesture, out var modes)) users[gesture] = modes = [];
                modes.Add(mode);
            }
        }
        var result = new Dictionary<CaptureMode, IReadOnlyList<string>>();
        foreach (var (gesture, modes) in users.Where(u => u.Value.Count > 1))
        {
            foreach (var mode in modes)
            {
                var list = result.TryGetValue(mode, out var existing) ? existing.ToList() : [];
                list.Add(gesture.ToString());
                result[mode] = list;
            }
        }
        return result;
    }
}
