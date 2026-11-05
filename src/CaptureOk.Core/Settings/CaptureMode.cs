namespace CaptureOk.Core.Settings;

public enum CaptureMode
{
    /// <summary>영역 지정 (드래그 + 자동 감지)</summary>
    Region,
    /// <summary>창 캡처 (마우스로 창 선택)</summary>
    Window,
    /// <summary>요소 캡처 (화면 요소 선택 전용)</summary>
    Element,
    FullScreen,
    Scroll,
    /// <summary>고정 크기</summary>
    FixedSize,
    /// <summary>직전 영역 다시</summary>
    LastRegion,
}
