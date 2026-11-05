using System.Windows.Interop;
using CaptureOk.Core.Hotkeys;
using CaptureOk.Core.Settings;
using CaptureOk.Native;

namespace CaptureOk.Hotkeys;

/// <summary>숨은 메시지 창으로 전역 단축키를 등록하고 눌림을 알린다.</summary>
internal sealed class HotkeyRegistrar : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<int, CaptureMode> _ids = [];
    private int _nextId = 1;

    public HotkeyRegistrar()
    {
        _source = new HwndSource(new HwndSourceParameters("CaptureOkHotkeys") { ParentWindow = NativeMethods.HWND_MESSAGE });
        _source.AddHook(WndProc);
    }

    public event Action<CaptureMode>? Pressed;

    /// <returns>등록하지 못한 단축키 표기 목록 (다른 프로그램이 이미 쓰는 키 등)</returns>
    public IReadOnlyList<string> RegisterAll(IReadOnlyDictionary<CaptureMode, List<string>> map)
    {
        UnregisterAll();
        var failed = new List<string>();
        foreach (var (mode, gestures) in map)
        {
            foreach (var text in gestures)
            {
                if (!HotkeyGesture.TryParse(text, out var g)) { failed.Add(text); continue; }
                var id = _nextId++;
                if (NativeMethods.RegisterHotKey(_source.Handle, id, (uint)g.Modifiers | NativeMethods.MOD_NOREPEAT, (uint)g.VirtualKey))
                    _ids[id] = mode;
                else
                    failed.Add(g.ToString());
            }
        }
        return failed;
    }

    private void UnregisterAll()
    {
        foreach (var id in _ids.Keys) NativeMethods.UnregisterHotKey(_source.Handle, id);
        _ids.Clear();
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && _ids.TryGetValue((int)wParam, out var mode))
        {
            handled = true;
            Pressed?.Invoke(mode);
        }
        return 0;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
