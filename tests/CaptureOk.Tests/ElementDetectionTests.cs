using CaptureOk.Core.Geometry;
using CaptureOk.Core.Selection;
using CaptureOk.Core.Settings;
using CaptureOk.Core.Windows;

namespace CaptureOk.Tests;

public class ElementDetectionTests
{
    private static RegionSelection Pressed(int x, int y)
    {
        var s = new RegionSelection(ScreenLayoutTests.TwoSideBySide(), confirmOnRelease: true);
        s.MouseMove(x, y, shift: false);
        s.MouseDown(x, y);
        return s;
    }

    [Fact(DisplayName = "click-with-jitter-is-a-click")]
    public void ClickWithJitterIsAClick()
    {
        var s = Pressed(100, 100);
        s.MouseMove(102, 103, shift: false); // 손떨림
        Assert.True(s.MouseUp(102, 103, shift: false));
        Assert.Equal(SelectionPhase.Hovering, s.Phase);
        Assert.Null(s.Rect);
    }

    [Fact(DisplayName = "drag-beyond-tolerance-is-a-drag")]
    public void DragBeyondToleranceIsADrag()
    {
        var s = Pressed(100, 100);
        s.MouseMove(104, 100, shift: false);
        Assert.False(s.MouseUp(104, 100, shift: false));
        Assert.Equal(SelectionPhase.Confirmed, s.Phase);
        Assert.Equal(new PixelRect(100, 100, 5, 1), s.Rect);
    }

    private static readonly PixelRect Window = new(0, 0, 800, 600);
    private static readonly PixelRect Panel = new(10, 50, 300, 400);
    private static readonly PixelRect Button = new(20, 60, 80, 24);
    private static readonly PixelRect Tiny = new(25, 65, 3, 3);

    // ---- 스크롤 캡처: 실제로 스크롤되는 영역부터 ----

    private static readonly PixelRect Content = new(10, 50, 600, 500); // 스크롤되는 목록·페이지 본문
    private static readonly PixelRect Row = new(20, 60, 560, 24);

    [Fact(DisplayName = "scroll-chain-starts-at-innermost-scrollable")]
    public void ScrollChainStartsAtInnermostScrollable()
    {
        var chain = new ElementChain(
            [new ElementLevel(Window, false), new ElementLevel(Content, true), new ElementLevel(Row, false)], preferScrollable: true);
        Assert.Equal(Content, chain.Current); // 목록의 한 줄이 아니라 목록 전체
        Assert.True(chain.HasScrollable);
        Assert.True(chain.Narrower());        // 휠로 안쪽도 고를 수 있다
        Assert.Equal(Row, chain.Current);
    }

    [Fact(DisplayName = "scroll-chain-without-scrollable-starts-at-window")]
    public void ScrollChainWithoutScrollableStartsAtWindow()
    {
        var chain = new ElementChain(
            [new ElementLevel(Window, false), new ElementLevel(Panel, false), new ElementLevel(Button, false)], preferScrollable: true);
        Assert.Equal(Window, chain.Current); // 스크롤 영역을 모르면 창 전체 (가운데에서 굴리면 대개 본문이 움직인다)
        Assert.False(chain.HasScrollable);    // 웹 브라우저는 처음 물을 때 아직 내용을 안 알려 주므로 다시 묻는다
    }

    [Fact(DisplayName = "same-bounds-keep-scrollable-flag")]
    public void SameBoundsKeepScrollableFlag()
    {
        // 같은 경계의 바깥 틀(스크롤 안 됨)과 안쪽 목록(스크롤됨)은 한 단계 — 스크롤된다고 본다
        var chain = new ElementChain(
            [new ElementLevel(Window, false), new ElementLevel(Content, false), new ElementLevel(Content, true), new ElementLevel(Row, false)],
            preferScrollable: true);
        Assert.Equal(Content, chain.Current);
        Assert.True(chain.Wider());
        Assert.Equal(Window, chain.Current);
    }

    // ---- 영역 지정 중 가운데 클릭: 강조 중인 작은 요소가 아니라 스크롤되는 영역 ----

    private static ElementChain RegionChain() =>
        new([new ElementLevel(Window, false), new ElementLevel(Content, true), new ElementLevel(Row, false)]);

    [Fact(DisplayName = "scroll-target-is-innermost-scrollable-not-highlighted-row")]
    public void ScrollTargetIsInnermostScrollableNotHighlightedRow()
    {
        var chain = RegionChain();
        Assert.Equal(Row, chain.Current);                     // 영역 지정은 가장 안쪽을 강조한다
        Assert.Equal(Content, chain.ScrollTargetAt(100, 70)); // 가운데 클릭은 그걸 담은 스크롤 영역
    }

    [Fact(DisplayName = "scroll-target-without-scrollable-is-window")]
    public void ScrollTargetWithoutScrollableIsWindow()
    {
        var chain = new ElementChain([Window, Panel, Button]);
        Assert.Equal(Window, chain.ScrollTargetAt(30, 70));
    }

    [Fact(DisplayName = "scroll-target-follows-level-picked-with-wheel")]
    public void ScrollTargetFollowsLevelPickedWithWheel()
    {
        var chain = RegionChain();
        Assert.True(chain.Wider());
        Assert.True(chain.Wider());
        Assert.Equal(Window, chain.ScrollTargetAt(100, 70)); // 휠로 직접 고른 범위가 우선
    }

    [Fact(DisplayName = "chain-knows-whether-wheel-picked-a-level")]
    public void ChainKnowsWhetherWheelPickedALevel()
    {
        var chain = RegionChain();
        Assert.False(chain.PickedWithWheel);
        Assert.False(chain.Narrower()); // 움직이지 못했으면 고른 것이 아니다
        Assert.False(chain.PickedWithWheel);
        Assert.True(chain.Wider());
        Assert.True(chain.PickedWithWheel);
    }

    [Fact(DisplayName = "scroll-target-ignores-levels-away-from-cursor")]
    public void ScrollTargetIgnoresLevelsAwayFromCursor()
    {
        var chain = RegionChain();
        Assert.Equal(Window, chain.ScrollTargetAt(700, 580)); // 스크롤 영역 밖으로 나갔으면 창 전체
        Assert.Null(chain.ScrollTargetAt(5000, 5000));        // 창 밖이면 없음
    }

    [Fact(DisplayName = "chain-starts-at-innermost-usable")]
    public void ChainStartsAtInnermostUsable()
    {
        var chain = new ElementChain([Window, Panel, Button]);
        Assert.Equal(Button, chain.Current);
    }

    [Fact(DisplayName = "chain-skips-tiny")]
    public void ChainSkipsTiny()
    {
        var chain = new ElementChain([Window, Panel, Button, Tiny]);
        Assert.Equal(Button, chain.Current); // 4px 미만은 고르지 않는다
        Assert.False(chain.Narrower());
        Assert.Null(new ElementChain([]).Current);
    }

    [Fact(DisplayName = "chain-wheel-moves-and-stops")]
    public void ChainWheelMovesAndStops()
    {
        var chain = new ElementChain([Window, Panel, Button]);
        Assert.True(chain.Wider());
        Assert.Equal(Panel, chain.Current);
        Assert.True(chain.Wider());
        Assert.Equal(Window, chain.Current);
        Assert.Equal(Button, chain.Innermost); // 고른 단계와 상관없이 가장 안쪽
        Assert.False(chain.Wider());   // 가장 바깥에서 멈춤
        Assert.True(chain.Narrower());
        Assert.Equal(Panel, chain.Current);
    }

    [Fact(DisplayName = "chain-drops-duplicate-bounds")]
    public void ChainDropsDuplicateBounds()
    {
        var chain = new ElementChain([Window, Window, Panel, Panel]); // 같은 경계의 겹친 요소는 한 단계
        Assert.Equal(Panel, chain.Current);
        Assert.True(chain.Wider());
        Assert.Equal(Window, chain.Current);
        Assert.False(chain.Wider());
    }

    [Theory(DisplayName = "rule-matches-process-class-title")]
    [InlineData("recorder.exe", null, null, "Recorder", "Any", "any", true)]   // 프로세스 이름 (확장자·대소문자 무시)
    [InlineData(null, "BorderWnd", null, "app", "borderwnd", "x", true)]   // 창 클래스도 대소문자 무시
    [InlineData(null, null, "^공유 중", "app", "c", "공유 중 - 회의", true)]
    [InlineData("app", "Other", null, "app", "BorderWnd", "x", false)]           // 적힌 조건은 모두 맞아야 함
    [InlineData(null, null, null, "app", "c", "t", false)]                        // 빈 규칙은 아무것도 빼지 않음
    public void RuleMatchesProcessClassTitle(string? process, string? cls, string? title, string p, string c, string t, bool expected) =>
        Assert.Equal(expected, new ExclusionRule(process, cls, title).Matches(p, c, t));

    [Fact(DisplayName = "invalid-regex-rule-is-ignored")]
    public void InvalidRegexRuleIsIgnored()
    {
        Assert.False(new ExclusionRule(null, null, "([").Matches("app", "c", "(["));
        Assert.True(new ExclusionRule("app", null, "([").Matches("app", "c", "x")); // 정규식만 무시하고 나머지 조건으로
    }

    [Fact(DisplayName = "capture-page-reset-includes-auto-detect")]
    public void CapturePageResetIncludesAutoDetect()
    {
        var s = CaptureSettings.CreateDefault(@"C:\Users\tester");
        Assert.True(s.AutoDetectElements);
        Assert.Empty(s.ExcludedWindows);
        s.AutoDetectElements = false;
        SettingsPages.Reset(s, SettingsPage.Capture, @"C:\Users\tester");
        Assert.True(s.AutoDetectElements);
    }

    [Fact(DisplayName = "drag-back-to-start-is-not-a-click")]
    public void DragBackToStartIsNotAClick()
    {
        var s = Pressed(100, 100);
        s.MouseMove(160, 140, shift: false); // 끌었다가
        s.MouseMove(101, 101, shift: false); // 거의 제자리로 돌아와 놓음
        Assert.False(s.MouseUp(101, 101, shift: false));
    }

    [Fact(DisplayName = "chain-falls-back-to-level-containing-cursor")]
    public void ChainFallsBackToLevelContainingCursor()
    {
        var chain = new ElementChain([Window, Panel, Button]);
        Assert.Equal(Button, chain.CurrentContaining(30, 70));  // 버튼 안
        Assert.Equal(Panel, chain.CurrentContaining(200, 300)); // 버튼 밖, 패널 안 = 패널
        Assert.Equal(Window, chain.CurrentContaining(700, 500));
        Assert.Null(chain.CurrentContaining(900, 900));         // 창 밖
    }
}