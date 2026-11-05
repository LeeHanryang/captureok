using CaptureOk.Core.Selection;
using CaptureOk.Core.Settings;

namespace CaptureOk.Tests;

public class CaptureHintTests
{
    [Fact(DisplayName = "hint-region: 영역 지정 안내에 네 가지 조작과 취소가 모두 있다")]
    public void RegionListsAllActions()
    {
        var hint = CaptureHint.For(CaptureMode.Region, autoDetect: true)!;

        Assert.Contains("클릭", hint);
        Assert.Contains("드래그", hint);
        Assert.Contains("휠 클릭: 스크롤", hint);
        Assert.Contains("우클릭: 메뉴", hint);
        Assert.Contains("GIF", hint);
        Assert.Contains("Esc", hint);
    }

    [Theory(DisplayName = "hint-dragging: 끄는 동안에는 놓을 때의 동작과 누른 채 우클릭을 안내한다")]
    [InlineData(true, "놓으면 캡처")]
    [InlineData(false, "놓으면 범위 확정")]
    public void DraggingHint(bool confirmOnRelease, string release)
    {
        var hint = CaptureHint.ForDragging(confirmOnRelease);

        Assert.Contains(release, hint);
        Assert.Contains("누른 채 우클릭: 메뉴", hint);
        Assert.Contains("GIF", hint);
        Assert.Contains("Esc", hint);
    }

    [Fact(DisplayName = "hint-region-no-detect: 자동 감지를 끄면 강조된 부분 클릭 안내를 빼고, 나머지는 그대로")]
    public void RegionWithoutDetection()
    {
        var hint = CaptureHint.For(CaptureMode.Region, autoDetect: false)!;

        Assert.DoesNotContain("강조", hint);
        Assert.Contains("드래그", hint);
        Assert.Contains("우클릭: 메뉴", hint);
    }

    [Fact(DisplayName = "hint-element: 요소 캡처도 우클릭 메뉴를 안내하고, 안 되는 드래그는 말하지 않는다")]
    public void ElementHasMenuNotDrag()
    {
        var hint = CaptureHint.For(CaptureMode.Element, autoDetect: false)!;

        Assert.Contains("우클릭: 메뉴", hint);
        Assert.DoesNotContain("드래그", hint);
    }

    [Theory(DisplayName = "hint-no-menu: 우클릭 메뉴가 없는 방식은 메뉴를 안내하지 않는다")]
    [InlineData(CaptureMode.Window)]
    [InlineData(CaptureMode.Scroll)]
    [InlineData(CaptureMode.FixedSize)]
    public void OtherModesHaveNoMenu(CaptureMode mode)
    {
        var hint = CaptureHint.For(mode, autoDetect: true)!;

        Assert.Contains("Esc", hint);
        Assert.DoesNotContain("메뉴", hint);
    }

    [Theory(DisplayName = "hint-instant: 고르는 화면이 없는 방식은 안내도 없다")]
    [InlineData(CaptureMode.FullScreen)]
    [InlineData(CaptureMode.LastRegion)]
    public void InstantModesHaveNoHint(CaptureMode mode) =>
        Assert.Null(CaptureHint.For(mode, autoDetect: true));
}
