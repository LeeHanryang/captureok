using CaptureOk.Core.Settings;

namespace CaptureOk.Core.Selection;

/// <summary>영역을 고르는 화면에 보여 줄 조작 안내. 고르는 화면이 없는 방식은 null.</summary>
public static class CaptureHint
{
    private const string Menu = "우클릭: 메뉴 (GIF 녹화)";
    private const string Wheel = "휠: 범위 넓히기·좁히기";
    private const string Cancel = "Esc: 취소";

    /// <summary>영역 지정에서 끄는 동안의 안내.</summary>
    public static string ForDragging(bool confirmOnRelease) =>
        $"{(confirmOnRelease ? "놓으면 캡처" : "놓으면 범위 확정")}  ·  누른 채 {Menu}  ·  {Cancel}";

    public static string? For(CaptureMode mode, bool autoDetect) => mode switch
    {
        CaptureMode.Region when autoDetect => $"클릭: 강조된 부분  ·  드래그: 범위 지정  ·  {Wheel}  ·  휠 클릭: 스크롤 캡처  ·  {Menu}  ·  {Cancel}",
        CaptureMode.Region => $"드래그: 범위 지정  ·  휠 클릭: 스크롤 캡처  ·  {Menu}  ·  {Cancel}",
        CaptureMode.Element => $"클릭: 강조된 부분  ·  {Wheel}  ·  {Menu}  ·  {Cancel}",
        CaptureMode.Scroll => $"클릭: 스크롤할 곳 고르기  ·  {Wheel}  ·  {Cancel}",
        CaptureMode.Window => $"클릭: 창 고르기  ·  {Cancel}",
        CaptureMode.FixedSize => $"클릭: 캡처  ·  숫자 1~9: 크기 바꾸기  ·  {Cancel}",
        _ => null,
    };
}
