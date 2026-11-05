using CaptureOk.Core.Imaging;

namespace CaptureOk.Tests;

public class ScrollStitcherTests
{
    private const int W = 40;

    /// <summary>행마다 다른 무늬의 긴 페이지 (BGRA). blank 구간은 단색 행.</summary>
    private static byte[] Page(int height, int seed = 1, (int From, int To)? blank = null)
    {
        var rnd = new Random(seed);
        var pixels = new byte[W * height * 4];
        for (var y = 0; y < height; y++)
        {
            var isBlank = blank is { } b && y >= b.From && y < b.To;
            for (var x = 0; x < W; x++)
            {
                var i = (y * W + x) * 4;
                pixels[i] = isBlank ? (byte)250 : (byte)rnd.Next(256);
                pixels[i + 1] = isBlank ? (byte)250 : (byte)rnd.Next(256);
                pixels[i + 2] = isBlank ? (byte)250 : (byte)rnd.Next(256);
                pixels[i + 3] = 255;
            }
        }
        return pixels;
    }

    /// <summary>창 높이 h로 page의 offset 위치를 본 화면. 위 header행·아래 footer행은 고정 막대(모든 화면에서 같음).</summary>
    private static byte[] Frame(byte[] page, int offset, int h, byte[] header, byte[] footer)
    {
        var frame = new byte[W * h * 4];
        var headerRows = header.Length / (W * 4);
        var footerRows = footer.Length / (W * 4);
        Buffer.BlockCopy(header, 0, frame, 0, header.Length);
        var contentRows = h - headerRows - footerRows;
        Buffer.BlockCopy(page, offset * W * 4, frame, headerRows * W * 4, contentRows * W * 4);
        Buffer.BlockCopy(footer, 0, frame, (h - footerRows) * W * 4, footer.Length);
        return frame;
    }

    private static byte[] Rows(byte[] pixels, int from, int count) => pixels.AsSpan(from * W * 4, count * W * 4).ToArray();

    [Fact(DisplayName = "stitches-frames-with-fixed-header-and-footer")]
    public void StitchesFramesWithFixedHeaderAndFooter()
    {
        const int h = 100, headerRows = 10, footerRows = 8; // 가운데 내용 82줄
        var page = Page(400);
        var header = Page(headerRows, seed: 7);
        var footer = Page(footerRows, seed: 9);
        var stitcher = new ScrollStitcher(W, h, maxHeight: 30000);

        var offsets = new[] { 0, 37, 74, 111, 148, 185, 222, 259, 296, 318 }; // 마지막은 끝에 닿아 덜 움직임 (400 - 82)
        foreach (var offset in offsets) Assert.Equal(StitchStep.Appended, stitcher.Add(Frame(page, offset, h, header, footer)));
        Assert.Equal(StitchStep.Ended, stitcher.Add(Frame(page, 318, h, header, footer))); // 더 안 움직임 = 끝

        var (pixels, width, height) = stitcher.Build();
        Assert.Equal(W, width);
        Assert.Equal(headerRows + 400 + footerRows, height);
        Assert.Equal(header, Rows(pixels, 0, headerRows));
        Assert.Equal(page, Rows(pixels, headerRows, 400));
        Assert.Equal(footer, Rows(pixels, headerRows + 400, footerRows));
    }

    [Fact(DisplayName = "stitches-through-blank-rows")]
    public void StitchesThroughBlankRows()
    {
        const int h = 60;
        var page = Page(300, blank: (100, 130)); // 빈 줄 30행 (화면 높이의 절반)
        var stitcher = new ScrollStitcher(W, h, maxHeight: 30000);
        foreach (var offset in new[] { 0, 25, 50, 75, 100, 125, 150, 175, 200, 225, 240 })
            stitcher.Add(Rows(page, offset, h));

        var (pixels, _, height) = stitcher.Build();
        Assert.Equal(300, height);
        Assert.Equal(page, pixels);
    }

    [Fact(DisplayName = "unrelated-frame-fails")]
    public void UnrelatedFrameFails()
    {
        const int h = 60;
        var stitcher = new ScrollStitcher(W, h, maxHeight: 30000);
        stitcher.Add(Rows(Page(200), 0, h));
        Assert.Equal(StitchStep.NoOverlap, stitcher.Add(Rows(Page(200, seed: 99), 0, h)));
        Assert.Equal(h, stitcher.Build().Height); // 그때까지 찍은 것은 남는다
    }

    [Fact(DisplayName = "stops-at-max-height")]
    public void StopsAtMaxHeight()
    {
        const int h = 50;
        var page = Page(1000);
        var stitcher = new ScrollStitcher(W, h, maxHeight: 120);
        var last = StitchStep.Appended;
        for (var offset = 0; offset <= 900 && last == StitchStep.Appended; offset += 30) last = stitcher.Add(Rows(page, offset, h));

        Assert.Equal(StitchStep.LimitReached, last);
        var (pixels, _, height) = stitcher.Build();
        Assert.Equal(120, height);
        Assert.Equal(Rows(page, 0, 120), pixels);
    }

    [Fact(DisplayName = "last-shift-is-reported")]
    public void LastShiftIsReported()
    {
        const int h = 60;
        var page = Page(300);
        var stitcher = new ScrollStitcher(W, h, maxHeight: 30000);
        stitcher.Add(Rows(page, 0, h));
        stitcher.Add(Rows(page, 13, h));
        Assert.Equal(13, stitcher.LastShift); // 다음 스크롤 양을 정하는 데 쓴다
    }

    [Fact(DisplayName = "moving-scrollbar-is-ignored")]
    public void MovingScrollbarIsIgnored()
    {
        const int h = 80, bar = 4;
        var page = Page(500);
        var stitcher = new ScrollStitcher(W, h, maxHeight: 30000, ignoreRightColumns: bar);
        var rnd = new Random(5);
        foreach (var offset in new[] { 0, 40, 80, 120, 160 })
        {
            var frame = Rows(page, offset, h);
            for (var y = 0; y < h; y++) // 오른쪽 막대(스크롤 막대 손잡이)는 화면마다 다르게
                for (var x = W - bar; x < W; x++) frame[(y * W + x) * 4] = (byte)rnd.Next(256);
            Assert.NotEqual(StitchStep.NoOverlap, stitcher.Add(frame));
        }
        Assert.Equal(160 + h, stitcher.Height);
    }

    /// <summary>frame의 (x0..x1, y0..y1) 칸을 overlay로 덮는다 (고정된 사이드바·떠 있는 단추 등).</summary>
    private static byte[] Cover(byte[] frame, int x0, int x1, int y0, int y1, byte[] overlay)
    {
        var copy = (byte[])frame.Clone();
        for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
                for (var c = 0; c < 4; c++)
                    copy[(y * W + x) * 4 + c] = overlay[((y - y0) * (x1 - x0) + (x - x0)) * 4 + c];
        return copy;
    }

    private static byte[] Patch(int width, int height, int seed)
    {
        var rnd = new Random(seed);
        var p = new byte[width * height * 4];
        rnd.NextBytes(p);
        for (var i = 3; i < p.Length; i += 4) p[i] = 255;
        return p;
    }

    /// <summary>결과의 열 x0..x1이 page의 같은 열과 같은지.</summary>
    private static void AssertColumnsEqual(byte[] page, byte[] result, int rows, int x0, int x1)
    {
        for (var y = 0; y < rows; y++)
            for (var x = x0; x < x1; x++)
                for (var c = 0; c < 4; c++)
                    Assert.True(page[(y * W + x) * 4 + c] == result[(y * W + x) * 4 + c], $"({x},{y}) 다름");
    }

    [Fact(DisplayName = "fixed-sidebar-does-not-stop-stitching")]
    public void FixedSidebarDoesNotStopStitching()
    {
        const int h = 100;
        var page = Page(600);
        var sidebar = Patch(10, h, seed: 3); // 왼쪽 10열은 스크롤해도 그대로
        var stitcher = new ScrollStitcher(W, h, maxHeight: 30000);
        foreach (var offset in new[] { 0, 40, 80, 120, 160, 200, 240, 280, 320, 360, 400, 440, 480, 500 })
            Assert.Equal(StitchStep.Appended, stitcher.Add(Cover(Rows(page, offset, h), 0, 10, 0, h, sidebar)));

        var (pixels, _, height) = stitcher.Build();
        Assert.Equal(600, height);
        AssertColumnsEqual(page, pixels, 600, 10, W);
    }

    [Fact(DisplayName = "floating-button-is-not-repeated")]
    public void FloatingButtonIsNotRepeated()
    {
        const int h = 100;
        var page = Page(600);
        var button = Patch(12, 15, seed: 4); // 오른쪽 아래에 떠 있는 단추 (처음부터, 4픽셀 단위 위치)
        var stitcher = new ScrollStitcher(W, h, maxHeight: 30000);
        foreach (var offset in new[] { 0, 30, 60, 90, 120, 150, 180, 210, 240, 270, 300, 330, 360, 390, 420, 450, 480, 500 })
            Assert.Equal(StitchStep.Appended, stitcher.Add(Cover(Rows(page, offset, h), 24, 36, 70, 85, button)));

        var (pixels, _, height) = stitcher.Build();
        Assert.Equal(600, height);
        // 단추 밑에 가렸던 내용은 위로 올라와 보일 때 채워진다 — 마지막 화면의 단추 자리(570~585행)만 단추로 남는다
        Assert.Equal(Rows(page, 0, 570), Rows(pixels, 0, 570));
        Assert.Equal(Rows(page, 585, 15), Rows(pixels, 585, 15));
    }

    [Fact(DisplayName = "button-that-appears-later-does-not-stop-stitching")]
    public void ButtonThatAppearsLaterDoesNotStopStitching()
    {
        const int h = 100;
        var page = Page(600);
        var button = Patch(10, 15, seed: 4); // 첫 스크롤 뒤에 나타나는 "맨 위로" 단추
        var stitcher = new ScrollStitcher(W, h, maxHeight: 30000);
        var offsets = new[] { 0, 30, 60, 90, 120, 150, 180, 210, 240, 270, 300, 330, 360, 390, 420, 450, 480, 500 };
        foreach (var offset in offsets)
        {
            var frame = Rows(page, offset, h);
            Assert.Equal(StitchStep.Appended, stitcher.Add(offset == 0 ? frame : Cover(frame, 25, 35, 70, 85, button)));
        }
        Assert.Equal(600, stitcher.Height);
    }

    [Fact(DisplayName = "sticky-header-that-appears-after-scrolling")]
    public void StickyHeaderThatAppearsAfterScrolling()
    {
        const int h = 200;
        var page = Page(1000);
        var header = Patch(W, 15, seed: 6); // 스크롤하면 위에 붙는 머리글 (첫 화면에는 없음)
        var stitcher = new ScrollStitcher(W, h, maxHeight: 30000);
        for (var offset = 0; offset <= 800; offset += 80)
        {
            var frame = Rows(page, offset, h);
            Assert.Equal(StitchStep.Appended, stitcher.Add(offset == 0 ? frame : Cover(frame, 0, W, 0, 15, header)));
        }

        var (pixels, _, height) = stitcher.Build();
        Assert.Equal(1000, height);
        Assert.Equal(page, pixels);
    }

    [Fact(DisplayName = "blank-rows-at-bottom-are-not-a-footer")]
    public void BlankRowsAtBottomAreNotAFooter()
    {
        const int h = 100;
        var page = Page(400, blank: (90, 110)); // 처음 두 화면이 모두 빈 줄로 끝남
        var stitcher = new ScrollStitcher(W, h, maxHeight: 30000);
        foreach (var offset in new[] { 0, 10, 50, 90, 130, 170, 210, 250, 290, 300 })
            stitcher.Add(Rows(page, offset, h));

        var (pixels, _, height) = stitcher.Build();
        Assert.Equal(400, height);
        Assert.Equal(page, pixels); // 마지막 화면의 아래 내용이 빠지지 않는다
    }

    [Fact(DisplayName = "repetitive-content-prefers-expected-shift")]
    public void RepetitiveContentPrefersExpectedShift()
    {
        const int h = 100, period = 10;
        var unit = Page(period, seed: 8);
        var page = new byte[W * 500 * 4];
        for (var y = 0; y < 500; y++) Buffer.BlockCopy(unit, (y % period) * W * 4, page, y * W * 4, W * 4);
        var stitcher = new ScrollStitcher(W, h, maxHeight: 30000);

        stitcher.Add(Rows(page, 0, h));
        stitcher.Add(Rows(page, 47, h), expectedShift: 47);
        Assert.Equal(47, stitcher.LastShift); // 37·27·17·7도 똑같이 맞지만 스크롤한 양에 가까운 것
    }

    [Fact(DisplayName = "animation-at-the-end-is-treated-as-end")]
    public void AnimationAtTheEndIsTreatedAsEnd()
    {
        const int h = 100;
        var page = Page(300);
        var stitcher = new ScrollStitcher(W, h, maxHeight: 30000);
        stitcher.Add(Rows(page, 0, h));
        stitcher.Add(Rows(page, 50, h));
        // 더 안 움직였고 작은 움직이는 그림(깜박이는 커서 등)만 바뀜
        Assert.Equal(StitchStep.Ended, stitcher.Add(Cover(Rows(page, 50, h), 5, 15, 20, 26, Patch(10, 6, seed: 11))));
    }

    [Fact(DisplayName = "blank-overlap-uses-last-shift")]
    public void BlankOverlapUsesLastShift()
    {
        const int h = 60;
        var page = Page(300, blank: (60, 100)); // 겹치는 곳이 모두 빈 줄인 화면이 생김
        var stitcher = new ScrollStitcher(W, h, maxHeight: 30000);
        foreach (var offset in new[] { 0, 30, 60, 90, 120, 150, 180, 210, 240 })
            Assert.Equal(StitchStep.Appended, stitcher.Add(Rows(page, offset, h)));

        var (pixels, _, height) = stitcher.Build();
        Assert.Equal(300, height);
        Assert.Equal(page, pixels);
    }

    [Fact(DisplayName = "copy-to-matches-build")]
    public void CopyToMatchesBuild()
    {
        const int h = 100, headerRows = 10, footerRows = 8;
        var page = Page(400);
        var header = Page(headerRows, seed: 7);
        var footer = Page(footerRows, seed: 9);
        var stitcher = new ScrollStitcher(W, h, maxHeight: 30000);
        foreach (var offset in new[] { 0, 37, 74, 111, 148 }) stitcher.Add(Frame(page, offset, h, header, footer));

        var (pixels, _, height) = stitcher.Build();
        var copy = new byte[height * W * 4];
        stitcher.CopyTo(copy, W * 4);
        Assert.Equal(pixels, copy);
    }

    [Fact(DisplayName = "not-scrolled-reports-no-progress")]
    public void NotScrolledReportsNoProgress()
    {
        const int h = 60;
        var page = Page(300);
        var stitcher = new ScrollStitcher(W, h, maxHeight: 30000);
        stitcher.Add(Rows(page, 0, h));
        Assert.Equal(StitchStep.Ended, stitcher.Add(Rows(page, 0, h)));
        Assert.False(stitcher.Scrolled);
        stitcher.Add(Rows(page, 20, h));
        Assert.True(stitcher.Scrolled);
    }

    [Fact(DisplayName = "portal-page-with-side-column-at-other-speed")]
    public void PortalPageWithSideColumnAtOtherSpeed()
    {
        // 포털 첫 화면처럼: 양옆은 빈 여백, 본문(4~31열)은 그대로 스크롤, 오른쪽 열(32~35열)은 4분의 1 속도, 스크롤하면 위에 머리글이 붙음
        const int h = 200;
        var main = Page(1000, seed: 21);
        var side = Page(400, seed: 22);
        var header = Patch(W, 10, seed: 23);
        byte[] View(int offset)
        {
            var frame = new byte[W * h * 4];
            for (var i = 0; i < frame.Length; i += 4) { frame[i] = frame[i + 1] = frame[i + 2] = 30; frame[i + 3] = 255; }
            for (var y = 0; y < h; y++)
            {
                Buffer.BlockCopy(main, ((offset + y) * W + 4) * 4, frame, (y * W + 4) * 4, 28 * 4);
                Buffer.BlockCopy(side, ((offset / 4 + y) * W + 32) * 4, frame, (y * W + 32) * 4, 4 * 4);
            }
            return offset == 0 ? frame : Cover(frame, 0, W, 0, 10, header);
        }
        var stitcher = new ScrollStitcher(W, h, maxHeight: 30000);
        for (var offset = 0; offset <= 800; offset += 80)
            Assert.Equal(StitchStep.Appended, stitcher.Add(View(offset)));

        var (pixels, _, height) = stitcher.Build();
        Assert.Equal(1000, height);
        AssertColumnsEqual(main, pixels, 1000, 4, 32); // 본문은 빠짐·겹침 없이
    }

    [Fact(DisplayName = "end-frame-does-not-change-bars")]
    public void EndFrameDoesNotChangeBars()
    {
        const int h = 100;
        var page = Page(300);
        var stitcher = new ScrollStitcher(W, h, maxHeight: 30000);
        stitcher.Add(Rows(page, 0, h));
        stitcher.Add(Rows(page, 50, h));
        // 끝에 닿아 안 움직였고 가운데 작은 그림만 바뀜 — 위·아래가 모두 그대로라 막대로 잘못 보기 쉽다
        Assert.Equal(StitchStep.Ended, stitcher.Add(Cover(Rows(page, 50, h), 5, 15, 48, 52, Patch(10, 4, seed: 12))));

        var (pixels, _, height) = stitcher.Build();
        Assert.Equal(150, height);
        Assert.Equal(Rows(page, 0, 150), pixels);
    }

    [Fact(DisplayName = "appearing-header-is-not-pasted-over-blank-margins")]
    public void AppearingHeaderIsNotPastedOverBlankMargins()
    {
        // 양옆 빈 여백(단색) + 본문. 스크롤하면 위에 머리글이 미끄러져 나오며(두 화면에 걸쳐 모양이 바뀜) 붙는다.
        const int h = 100;
        var main = Page(700, seed: 31);
        var sliding = Patch(W, 10, seed: 32);
        var settled = Patch(W, 10, seed: 33);
        byte[] View(int offset, byte[]? header)
        {
            var frame = new byte[W * h * 4];
            for (var i = 0; i < frame.Length; i += 4) { frame[i] = frame[i + 1] = frame[i + 2] = 30; frame[i + 3] = 255; }
            for (var y = 0; y < h; y++) Buffer.BlockCopy(main, ((offset + y) * W + 4) * 4, frame, (y * W + 4) * 4, 32 * 4);
            return header is null ? frame : Cover(frame, 0, W, 0, 10, header);
        }
        var stitcher = new ScrollStitcher(W, h, maxHeight: 30000);
        stitcher.Add(View(0, null));
        stitcher.Add(View(40, sliding));
        for (var offset = 80; offset <= 600; offset += 40) stitcher.Add(View(offset, settled));

        var (pixels, _, height) = stitcher.Build();
        Assert.Equal(700, height);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < 4; x++)
                Assert.True(pixels[(y * W + x) * 4] == 30, $"({x},{y}) 여백에 머리글이 찍힘");
        AssertColumnsEqual(main, pixels, 700, 4, 36);
    }

    [Fact(DisplayName = "list-rows-that-differ-only-by-name")]
    public void ListRowsThatDifferOnlyByName()
    {
        // 파일 목록처럼: 줄 높이 7, 이름 열(0~7열)만 줄마다 다르고 날짜·종류·크기 열은 모든 줄이 같다.
        // 한 줄만큼 어긋난 이동량도 80%쯤 맞지만, 진짜 이동량은 다 맞는다.
        const int h = 100, rowHeight = 7;
        var names = Page(800, seed: 41);
        var line = Page(rowHeight, seed: 42);
        var page = new byte[W * 800 * 4];
        for (var y = 0; y < 800; y++)
        {
            Buffer.BlockCopy(names, y * W * 4, page, y * W * 4, 8 * 4);
            Buffer.BlockCopy(line, ((y % rowHeight) * W + 8) * 4, page, (y * W + 8) * 4, (W - 8) * 4);
        }
        var stitcher = new ScrollStitcher(W, h, maxHeight: 30000);
        foreach (var offset in new[] { 0, 33, 66, 99, 132, 165, 198 }) // 줄 높이의 배수가 아닌 이동 (탐색기 실제 화면과 같음)
            Assert.Equal(StitchStep.Appended, stitcher.Add(Rows(page, offset, h)));
        Assert.Equal(33, stitcher.LastShift);
        Assert.Equal(Rows(page, 0, 298), stitcher.Build().Pixels);
    }

    [Fact(DisplayName = "wide-list-scrolled-by-whole-rows")]
    public void WideListScrolledByWholeRows()
    {
        // 탐색기처럼: 넓은 목록(320열 = 32칸), 이름 끝자리(0~9열)만 줄마다 다르고 나머지는 모든 줄이 같다.
        // 휠 한 칸 = 정확히 몇 줄이라, 나머지 칸은 같은 자리에 그대로 → 비교할 칸이 이름 칸 하나뿐이다.
        const int width = 320, h = 200, rowHeight = 7, total = 900;
        var rnd = new Random(51);
        var line = new byte[rowHeight * width * 4];
        rnd.NextBytes(line);
        var page = new byte[total * width * 4];
        for (var y = 0; y < total; y++)
        {
            Buffer.BlockCopy(line, (y % rowHeight) * width * 4, page, y * width * 4, width * 4);
            for (var x = 0; x < 10; x++) for (var c = 0; c < 3; c++) page[(y * width + x) * 4 + c] = (byte)rnd.Next(256);
        }
        for (var i = 3; i < page.Length; i += 4) page[i] = 255;
        byte[] View(int offset) => page.AsSpan(offset * width * 4, h * width * 4).ToArray();

        var stitcher = new ScrollStitcher(width, h, maxHeight: 30000);
        foreach (var offset in new[] { 0, 63, 126, 189, 252, 315, 378, 441, 504, 567, 630, 693, 700 })
            Assert.Equal(StitchStep.Appended, stitcher.Add(View(offset))); // 예상 이동량 없이도
        Assert.Equal(StitchStep.Ended, stitcher.Add(View(700)));
        var (pixels, _, height) = stitcher.Build();
        Assert.Equal(total, height);
        Assert.Equal(page, pixels);
    }
}