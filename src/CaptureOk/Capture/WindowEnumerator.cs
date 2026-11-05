using System.Diagnostics;
using CaptureOk.Core.Geometry;
using CaptureOk.Core.Windows;
using CaptureOk.Native;

namespace CaptureOk.Capture;

/// <summary>
/// 캡처 순간의 최상위 창 목록 (z-order 앞쪽부터, 그림자 뺀 실제 경계).
/// 자기 창, 숨김·최소화·cloaked·도구 창, 클릭이 통과하는 투명 창(화면 녹화 테두리 등), 제외 규칙에 맞는 창은 뺀다.
/// </summary>
internal static class WindowEnumerator
{
    public static IReadOnlyList<WindowInfo> Snapshot(IReadOnlyList<ExclusionRule>? exclusions = null)
    {
        var ownPid = (uint)Environment.ProcessId;
        var windows = new List<WindowInfo>();
        var title = new char[256];
        var className = new char[256];
        var rules = exclusions ?? [];
        // 제외 규칙이 있을 때만, 프로세스 이름을 한 번에 모아 둔다 (창마다 묻지 않게)
        var processNames = rules.Count > 0 ? ProcessNames() : [];

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd) || NativeMethods.IsIconic(hWnd)) return true;
            NativeMethods.GetWindowThreadProcessId(hWnd, out var pid);
            if (pid == ownPid) return true;
            var exStyle = NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE);
            if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0) return true;
            // 클릭이 통과하는 투명 창(화면 녹화 테두리 등)은 두 표시가 함께 있을 때만 확실하다
            const long clickThrough = NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED;
            if ((exStyle & clickThrough) == clickThrough) return true;
            if (NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;

            NativeMethods.RECT r;
            if (NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out r, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.RECT>()) != 0
                && !NativeMethods.GetWindowRect(hWnd, out r)) return true;

            var bounds = new PixelRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
            if (bounds.IsEmpty) return true;

            var len = NativeMethods.GetWindowText(hWnd, title, title.Length);
            var text = new string(title, 0, Math.Max(0, len));
            if (rules.Count > 0)
            {
                var classLen = NativeMethods.GetClassName(hWnd, className, className.Length);
                var cls = new string(className, 0, Math.Max(0, classLen));
                var process = processNames.GetValueOrDefault(pid, "");
                if (rules.Any(rule => rule.Matches(process, cls, text))) return true;
            }
            windows.Add(new WindowInfo(hWnd, bounds, text));
            return true;
        }, 0);

        return windows;
    }

    private static Dictionary<uint, string> ProcessNames()
    {
        var names = new Dictionary<uint, string>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try { names[(uint)process.Id] = process.ProcessName; }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
        }
        return names;
    }
}
