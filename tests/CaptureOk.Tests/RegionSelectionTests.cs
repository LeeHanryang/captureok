using CaptureOk.Core.Geometry;
using CaptureOk.Core.Selection;

namespace CaptureOk.Tests;

public class RegionSelectionTests
{
    private static RegionSelection Dragged(int x1, int y1, int x2, int y2, bool shift = false)
    {
        var s = new RegionSelection(ScreenLayoutTests.TwoSideBySide());
        s.MouseMove(x1, y1, shift: false);
        s.MouseDown(x1, y1);
        s.MouseMove(x2, y2, shift);
        s.MouseUp(x2, y2, shift);
        return s;
    }

    [Fact(DisplayName = "drag-creates-rect")]
    public void DragCreatesRect()
    {
        var s = Dragged(10, 10, 20, 30);
        Assert.Equal(SelectionPhase.Selected, s.Phase);
        Assert.Equal(new PixelRect(10, 10, 11, 21), s.Rect);
    }

    [Fact(DisplayName = "release-confirms-when-confirm-on-release")]
    public void ReleaseConfirmsWhenConfirmOnRelease()
    {
        var s = new RegionSelection(ScreenLayoutTests.TwoSideBySide(), confirmOnRelease: true);
        s.MouseMove(10, 10, shift: false);
        s.MouseDown(10, 10);
        s.Key(SelectionKey.Right, fast: false, alt: false); // 누른 채 방향키로 1px 미세 조정
        s.MouseMove(20, 30, shift: false);
        s.MouseUp(20, 30, shift: false);

        Assert.Equal(SelectionPhase.Confirmed, s.Phase);
        Assert.Equal(new PixelRect(10, 10, 11, 21), s.Rect);
    }

    [Fact(DisplayName = "click-without-drag-does-not-confirm-on-release")]
    public void ClickWithoutDragDoesNotConfirmOnRelease()
    {
        var s = new RegionSelection(ScreenLayoutTests.TwoSideBySide(), confirmOnRelease: true);
        s.MouseMove(10, 10, shift: false);
        s.MouseDown(10, 10);
        s.MouseUp(10, 10, shift: false);
        Assert.Equal(SelectionPhase.Hovering, s.Phase);
    }

    [Fact(DisplayName = "click-without-drag-stays-hovering")]
    public void ClickWithoutDragStaysHovering()
    {
        var s = Dragged(10, 10, 10, 10);
        Assert.Equal(SelectionPhase.Hovering, s.Phase);
        Assert.Null(s.Rect);
    }

    [Fact(DisplayName = "shift-square")]
    public void ShiftSquare()
    {
        Assert.Equal(new PixelRect(0, 0, 11, 11), Dragged(0, 0, 20, 10, shift: true).Rect);
    }

    [Fact(DisplayName = "arrow-hover-moves-cursor")]
    public void ArrowHoverMovesCursor()
    {
        var s = new RegionSelection(ScreenLayoutTests.TwoSideBySide());
        s.MouseMove(100, 100, shift: false);

        Assert.True(s.Key(SelectionKey.Right, fast: false, alt: false));
        Assert.Equal((101, 100), s.Cursor);
        Assert.True(s.Key(SelectionKey.Down, fast: true, alt: false));
        Assert.Equal((101, 110), s.Cursor);
    }

    [Fact(DisplayName = "arrow-selected-moves-rect")]
    public void ArrowSelectedMovesRect()
    {
        var s = Dragged(10, 10, 20, 30);
        s.Key(SelectionKey.Right, fast: false, alt: false);
        Assert.Equal(new PixelRect(11, 10, 11, 21), s.Rect);
        s.Key(SelectionKey.Down, fast: true, alt: false);
        Assert.Equal(new PixelRect(11, 20, 11, 21), s.Rect);
    }

    [Fact(DisplayName = "alt-arrow-resizes")]
    public void AltArrowResizes()
    {
        var s = Dragged(10, 10, 20, 30);
        s.Key(SelectionKey.Right, fast: false, alt: true);
        s.Key(SelectionKey.Up, fast: false, alt: true);
        Assert.Equal(new PixelRect(10, 10, 12, 20), s.Rect);
    }

    [Fact(DisplayName = "alt-arrow-resize-stays-inside-virtual-screen")]
    public void AltArrowResizeStaysInsideVirtualScreen()
    {
        var s = Dragged(5100, 1420, 5119, 1439); // 오른쪽·아래 가장자리에 붙은 선택
        s.Key(SelectionKey.Right, fast: false, alt: true);
        s.Key(SelectionKey.Down, fast: false, alt: true);
        Assert.Equal(new PixelRect(5100, 1420, 20, 20), s.Rect);
    }

    [Fact(DisplayName = "enter-confirms")]
    public void EnterConfirms()
    {
        var s = Dragged(10, 10, 20, 30);
        s.Key(SelectionKey.Enter, fast: false, alt: false);
        Assert.Equal(SelectionPhase.Confirmed, s.Phase);
    }

    [Fact(DisplayName = "double-click-confirms")]
    public void DoubleClickConfirms()
    {
        var s = Dragged(10, 10, 20, 30);
        s.DoubleClick();
        Assert.Equal(SelectionPhase.Confirmed, s.Phase);
    }

    [Fact(DisplayName = "double-click-inside-selection-confirms")]
    public void DoubleClickInsideSelectionConfirms()
    {
        var s = Dragged(10, 10, 20, 30);
        // 더블클릭의 첫 번째 클릭(누르고 떼기)이 선택을 지우면 안 된다
        s.MouseDown(15, 15);
        s.MouseUp(15, 15, shift: false);
        Assert.Equal(new PixelRect(10, 10, 11, 21), s.Rect);
        s.DoubleClick();
        Assert.Equal(SelectionPhase.Confirmed, s.Phase);
    }

    [Fact(DisplayName = "drag-inside-selection-moves-it")]
    public void DragInsideSelectionMovesIt()
    {
        var s = Dragged(10, 10, 20, 30);
        s.MouseDown(15, 15);
        s.MouseMove(115, 65, shift: false);
        s.MouseUp(115, 65, shift: false);
        Assert.Equal(SelectionPhase.Selected, s.Phase);
        Assert.Equal(new PixelRect(110, 60, 11, 21), s.Rect);
    }

    [Fact(DisplayName = "drag-outside-selection-starts-new")]
    public void DragOutsideSelectionStartsNew()
    {
        var s = Dragged(10, 10, 20, 30);
        s.MouseDown(100, 100);
        s.MouseMove(150, 120, shift: false);
        s.MouseUp(150, 120, shift: false);
        Assert.Equal(new PixelRect(100, 100, 51, 21), s.Rect);
    }

    [Fact(DisplayName = "escape-cancels")]
    public void EscapeCancels()
    {
        var s = Dragged(10, 10, 20, 30);
        s.Key(SelectionKey.Escape, fast: false, alt: false);
        Assert.Equal(SelectionPhase.Cancelled, s.Phase);
    }

    [Fact(DisplayName = "right-click-reselects")]
    public void RightClickReselects()
    {
        var s = Dragged(10, 10, 20, 30);
        s.Key(SelectionKey.RightButton, fast: false, alt: false);
        Assert.Equal(SelectionPhase.Hovering, s.Phase);
        Assert.Null(s.Rect);

        s.Key(SelectionKey.RightButton, fast: false, alt: false);
        Assert.Equal(SelectionPhase.Cancelled, s.Phase);
    }

    [Fact(DisplayName = "detach-drag: 끄는 중에 꺼내면 그 범위를 돌려주고 고르기 전으로 돌아간다")]
    public void DetachDragReturnsRange()
    {
        var s = new RegionSelection(ScreenLayoutTests.TwoSideBySide(), confirmOnRelease: true);
        s.MouseDown(10, 10);
        s.MouseMove(110, 60, shift: false);

        Assert.Equal(new PixelRect(10, 10, 101, 51), s.DetachDrag()); // 양 끝 픽셀 포함
        Assert.Equal(SelectionPhase.Hovering, s.Phase);
        Assert.Null(s.Rect);
        Assert.False(s.MouseUp(110, 60, shift: false)); // 뒤늦게 버튼을 떼도 확정되지 않는다
        Assert.Equal(SelectionPhase.Hovering, s.Phase);
    }

    [Fact(DisplayName = "detach-drag-shift: Shift로 맞춘 정사각형을 그대로 돌려준다")]
    public void DetachDragKeepsSquare()
    {
        var s = new RegionSelection(ScreenLayoutTests.TwoSideBySide(), confirmOnRelease: true);
        s.MouseDown(10, 10);
        s.MouseMove(110, 60, shift: true);

        Assert.Equal(new PixelRect(10, 10, 51, 51), s.DetachDrag());
    }

    [Fact(DisplayName = "detach-drag-click: 거의 안 끌었으면 범위 없이 고르기 전으로 돌아간다")]
    public void DetachDragWithinClickTolerance()
    {
        var s = new RegionSelection(ScreenLayoutTests.TwoSideBySide(), confirmOnRelease: true);
        s.MouseDown(10, 10);
        s.MouseMove(12, 11, shift: false);

        Assert.Null(s.DetachDrag());
        Assert.Equal(SelectionPhase.Hovering, s.Phase);
        Assert.Null(s.Rect);
    }

    [Fact(DisplayName = "detach-drag-back: 멀리 끌었다 제자리 근처로 와도 드래그다")]
    public void DetachDragAfterGoingFarAndBack()
    {
        var s = new RegionSelection(ScreenLayoutTests.TwoSideBySide(), confirmOnRelease: true);
        s.MouseDown(10, 10);
        s.MouseMove(200, 200, shift: false);
        s.MouseMove(12, 12, shift: false);

        Assert.NotNull(s.DetachDrag());
    }

    [Theory(DisplayName = "detach-drag-other-phase: 끄는 중이 아니면 아무것도 바꾸지 않는다")]
    [InlineData(false)]
    [InlineData(true)]
    public void DetachDragOutsideDragging(bool selected)
    {
        var s = selected ? Dragged(10, 10, 20, 30) : new RegionSelection(ScreenLayoutTests.TwoSideBySide());
        var phase = s.Phase;
        var rect = s.Rect;

        Assert.Null(s.DetachDrag());
        Assert.Equal(phase, s.Phase);
        Assert.Equal(rect, s.Rect);
    }

    [Fact(DisplayName = "cross-monitor-drag")]
    public void CrossMonitorDrag()
    {
        Assert.Equal(new PixelRect(2500, 100, 201, 201), Dragged(2500, 100, 2700, 300).Rect);
    }

    [Fact(DisplayName = "drag-clamped-to-virtual-screen")]
    public void DragClampedToVirtualScreen()
    {
        Assert.Equal(new PixelRect(5000, 1400, 120, 40), Dragged(5000, 1400, 6000, 2000).Rect);
    }

    [Fact(DisplayName = "fixed-size-follows-cursor")]
    public void FixedSizeFollowsCursor()
    {
        var s = new RegionSelection(ScreenLayoutTests.TwoSideBySide(), new PixelSize(1280, 720));

        s.MouseMove(1000, 1000, shift: false);
        Assert.Equal(new PixelRect(360, 640, 1280, 720), s.Rect);

        s.MouseMove(10, 10, shift: false); // 가장자리에서는 크기를 유지한 채 화면 안으로 밀어 넣는다
        Assert.Equal(new PixelRect(0, 0, 1280, 720), s.Rect);

        s.MouseDown(10, 10);
        Assert.Equal(SelectionPhase.Confirmed, s.Phase);
    }
}
