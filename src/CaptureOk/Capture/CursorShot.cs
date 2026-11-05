using CaptureOk.Native;

namespace CaptureOk.Capture;

/// <summary>찍는 순간의 마우스 커서 (복사한 커서 핸들과 그릴 왼쪽 위 위치, 가상 화면 좌표). 다 쓰면 Dispose.</summary>
internal sealed class CursorShot(nint handle, int x, int y) : IDisposable
{
    public nint Handle { get; private set; } = handle;
    public int X { get; } = x;
    public int Y { get; } = y;

    public void Dispose()
    {
        if (Handle == 0) return;
        NativeMethods.DestroyIcon(Handle);
        Handle = 0;
    }
}
