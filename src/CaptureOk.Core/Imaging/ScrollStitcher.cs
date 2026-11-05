namespace CaptureOk.Core.Imaging;

public enum StitchStep
{
    /// <summary>새로 보인 아래쪽 줄을 이어 붙였다.</summary>
    Appended,
    /// <summary>앞 화면과 같다(작은 움직이는 그림 정도만 다름) — 더 스크롤되지 않음(끝에 닿음).</summary>
    Ended,
    /// <summary>앞 화면과 겹치는 곳을 찾지 못했다 (화면이 통째로 바뀜 등). 그때까지 붙인 것은 남는다.</summary>
    NoOverlap,
    /// <summary>최대 길이에 닿아 거기까지만 붙였다.</summary>
    LimitReached,
}

/// <summary>
/// 스크롤하며 찍은 같은 크기의 화면(BGRA, 위에서 아래)들을 한 장으로 잇는다.
/// <list type="bullet">
/// <item>각 줄을 세로 띠(칸, 최대 32개) 여러 개로 나눠 칸 해시로 비교한다 — 줄 일부가 가려지거나 옆 열이 다르게 움직여도 나머지 칸으로 맞춘다.</item>
/// <item>앞 화면과 같은 자리에 그대로 있는 칸(고정된 사이드바·떠 있는 단추·고정 머리글)은 이동량 판정에서 뺀다.</item>
/// <item>위·아래 끝에서 같은 자리에 그대로인 줄은 고정 막대로 본다. 화면마다 다시 보고(나중에 붙는 머리글), 안쪽 끝의 빈 줄은 막대에 넣지 않는다.</item>
/// <item>고정된 것에 가려 찍힌 칸은, 그 내용이 스크롤되어 드러나면 진짜 내용으로 바꿔 그린다.</item>
/// <item>단색 칸(빈 여백)은 어느 위치와도 맞으므로 판정에서 뺀다.</item>
/// </list>
/// </summary>
/// <param name="ignoreRightColumns">비교에서 뺄 오른쪽 열 수 — 스크롤할 때마다 손잡이가 움직이는 스크롤 막대 (붙이는 그림에는 들어감)</param>
public sealed class ScrollStitcher
{
    /// <summary>한 줄을 나누는 세로 띠 수 (최대), 띠 하나의 최소 폭.</summary>
    private const int MaxSegments = 32, MinSegmentWidth = 8;
    /// <summary>
    /// 이동량으로 받아들일 최소 일치 비율과, 다른 후보보다 몇 배 잘 맞아야 하는지.
    /// 본문과 옆 열이 다른 속도로 스크롤되는 페이지(포털 첫 화면 등)도 본문 쪽이 뚜렷이 가장 잘 맞으면 받아들인다.
    /// </summary>
    private const double MinMatchRatio = 0.4, Dominance = 2.0;
    /// <summary>고정된 것(떠 있는 단추 등)을 가려 그리는 단위 (픽셀).</summary>
    private const int Block = 4;
    /// <summary>움직이지 않았다고 볼, 같은 자리에 그대로인 칸의 비율 (깜박이는 커서·움직이는 그림 정도는 봐준다).</summary>
    private const double EndedRatio = 0.9;
    /// <summary>앞 화면과 달라진 칸이 이보다 적으면 움직이지 않은 것으로 본다.</summary>
    private const double MinMovedRatio = 0.03;
    private const byte Unwritten = 0, FromFixed = 1, FromContent = 2;

    private readonly int _width, _height, _maxHeight;
    private readonly int _rowBytes;
    private readonly int _segments;
    private readonly int _blocks;
    private readonly int[] _segStart; // 칸 k의 비교 시작 열 (픽셀), _segStart[_segments] = 비교 끝 열
    private readonly List<byte[]> _rows = []; // 붙인 결과 (머리글 포함, 바닥글 제외)
    private readonly List<byte[]> _state = []; // 결과 줄의 4픽셀 단위마다 무엇으로 그렸는지
    private byte[]? _last; // 마지막으로 붙인 화면 (바닥글은 여기서)
    private ulong[]? _prevHash;
    private bool[]? _prevInfo;
    private int _prevOffset; // 앞 화면 0번째 줄이 결과의 몇 번째 줄인지
    private int _top, _bottom;
    private bool _barsKnown;

    public ScrollStitcher(int width, int height, int maxHeight, int ignoreRightColumns = 0)
    {
        _width = width;
        _height = height;
        _maxHeight = maxHeight;
        _rowBytes = width * 4;
        var compareWidth = Math.Max(1, width - Math.Clamp(ignoreRightColumns, 0, width - 1));
        var segWidth = Math.Max(MinSegmentWidth, (compareWidth + MaxSegments - 1) / MaxSegments);
        _segments = (compareWidth + segWidth - 1) / segWidth;
        _segStart = new int[_segments + 1];
        for (var k = 0; k < _segments; k++) _segStart[k] = k * segWidth;
        _segStart[_segments] = compareWidth;
        _blocks = (width + Block - 1) / Block;
    }

    /// <summary>마지막으로 붙일 때 찾은 이동량(줄). 다음 스크롤 양을 정하는 데 쓴다.</summary>
    public int LastShift { get; private set; }

    /// <summary>첫 화면 뒤로 한 번이라도 이어 붙였는지 (false면 스크롤되지 않은 것).</summary>
    public bool Scrolled { get; private set; }

    /// <summary>위·아래 고정 막대 줄 수 (기록용).</summary>
    public (int Top, int Bottom) Bars => (_top, _bottom);

    /// <summary>고정 막대를 뺀, 스크롤되는 구간의 높이.</summary>
    public int BandHeight => _height - _top - _bottom;

    public int Height => _last is null ? 0 : _barsKnown ? _rows.Count + _bottom : _height;

    /// <param name="expectedShift">이번에 스크롤했을 것으로 보는 줄 수 (모르면 0). 같은 무늬가 되풀이되는 내용에서 고르는 데 쓴다.</param>
    public StitchStep Add(byte[] frame, int expectedShift = 0)
    {
        if (frame.Length != _rowBytes * _height) throw new ArgumentException("화면 크기가 다릅니다", nameof(frame));
        var (hash, info) = HashCells(frame);

        if (_last is null)
        {
            _last = frame;
            Remember(hash, info);
            return StitchStep.Appended;
        }

        // 고정 막대는 이번 화면으로 이어 붙일 수 있을 때만 바꾼다 — 끝에 닿은(안 움직인) 화면은 위·아래가 모두 그대로라 막대로 잘못 보기 쉽다
        if (DetectBars(hash, info, _prevHash!) is not { } bars) return StitchStep.Ended; // 화면 전체가 같음
        var committed = (_top, _bottom);
        (_top, _bottom) = _barsKnown ? (Math.Max(_top, bars.Top), Math.Max(_bottom, bars.Bottom)) : bars;
        var shift = Match(hash, info, expectedShift, out var failure);
        if (shift <= 0)
        {
            (_top, _bottom) = committed;
            return failure;
        }
        if (!_barsKnown) StartBody();
        LastShift = shift;
        Scrolled = true;

        var limited = Paint(frame, shift);
        _last = frame;
        Remember(hash, info);
        return limited ? StitchStep.LimitReached : StitchStep.Appended;
    }

    public (byte[] Pixels, int Width, int Height) Build()
    {
        if (_last is null) return ([], _width, 0);
        var result = new byte[Height * _rowBytes];
        CopyTo(result, _rowBytes);
        return (result, _width, Height);
    }

    /// <summary>결과를 destination에 줄 간격 stride로 쓴다 (큰 그림을 한 번 더 복사하지 않게).</summary>
    public void CopyTo(Span<byte> destination, int stride)
    {
        if (_last is null) return;
        if (!_barsKnown)
        {
            for (var y = 0; y < _height; y++) _last.AsSpan(y * _rowBytes, _rowBytes).CopyTo(destination[(y * stride)..]);
            return;
        }
        for (var y = 0; y < _rows.Count; y++) _rows[y].CopyTo(destination[(y * stride)..]);
        for (var y = 0; y < _bottom; y++)
            _last.AsSpan((_height - _bottom + y) * _rowBytes, _rowBytes).CopyTo(destination[((_rows.Count + y) * stride)..]);
    }

    /// <summary>이번 화면과 앞 화면 사이의 이동량. 못 찾으면 0과 그 까닭(끝에 닿음·겹침 없음).</summary>
    private int Match(ulong[] hash, bool[] info, int expectedShift, out StitchStep failure)
    {
        failure = StitchStep.Ended;
        var band = BandHeight;
        if (band <= 0) return 0;
        var prevHash = _prevHash!;

        // 같은 자리에 그대로인 (정보 있는) 칸 = 고정된 것
        var fixedCell = new bool[hash.Length];
        int informative = 0, same = 0;
        for (var y = _top; y < _height - _bottom; y++)
            for (var k = 0; k < _segments; k++)
            {
                var c = y * _segments + k;
                if (!info[c] && !_prevInfo![c]) continue;
                informative++;
                if (hash[c] == prevHash[c]) { same++; fixedCell[c] = info[c]; }
            }
        // 움직인 칸이 거의 없으면(깜박이는 커서 정도) 끝에 닿은 것
        var movable = informative - same;
        if (informative == 0 || movable < informative * MinMovedRatio) return 0;

        // 이동량을 먼저 찾는다 — 줄 단위로 스크롤되는 목록은 날짜·종류 열이 같은 자리에 그대로라 "그대로인 칸"이 많아도 움직인 것이다
        var expected = expectedShift > 0 ? expectedShift : LastShift;
        var shift = FindShift(hash, info, fixedCell, band, expected, movable);
        if (shift > 0) return shift;
        if (same >= informative * EndedRatio) return 0;
        // 겹치는 곳이 거의 빈 여백이라 판정할 수 없으면, 지난번(또는 예상한) 이동량을 믿는다
        if (expected > 0 && expected < band && Score(hash, info, fixedCell, expected).Considered < MinConsidered(band, expected, movable))
            return expected;
        failure = StitchStep.NoOverlap;
        return 0;
    }

    /// <summary>
    /// 위·아래 끝에서 같은 자리에 그대로인 줄을 고정 막대로 본다(화면의 1/3까지, 안쪽 끝 빈 줄 제외).
    /// 화면 전체가 같으면 null.
    /// </summary>
    private (int Top, int Bottom)? DetectBars(ulong[] hash, bool[] info, ulong[] prevHash)
    {
        var top = 0;
        while (top < _height && RowEqual(hash, prevHash, top)) top++;
        if (top == _height) return null;
        var bottom = 0;
        while (bottom < _height - top && RowEqual(hash, prevHash, _height - 1 - bottom)) bottom++;
        top = Math.Min(top, _height / 3);
        bottom = Math.Min(bottom, _height / 3);
        while (top > 0 && !RowInformative(info, top - 1)) top--;
        while (bottom > 0 && !RowInformative(info, _height - bottom)) bottom--;
        return (top, bottom);
    }

    /// <summary>처음 이어 붙일 때: 첫 화면(바닥글 위까지)을 결과에 넣는다.</summary>
    private void StartBody()
    {
        _barsKnown = true;
        var first = _last!;
        for (var y = 0; y < _height - _bottom; y++)
        {
            _rows.Add(first.AsSpan(y * _rowBytes, _rowBytes).ToArray());
            _state.Add(Filled(FromContent));
        }
        _prevOffset = 0;
    }

    /// <summary>
    /// 이번 화면을 결과에 그린다: 새 줄은 덧붙이고, 전에 고정된 것으로 그렸던 곳은 이번에 보인 진짜 내용으로 바꾼다.
    /// 고정된 것 = 앞 화면과 같은 자리에 같은 픽셀 (4픽셀 단위, 단색 부분은 제외). 최대 길이에 닿았으면 true.
    /// </summary>
    private bool Paint(byte[] frame, int shift)
    {
        var prev = _last!;
        var offset = _prevOffset + shift;
        var fixedBlock = new bool[_height * _blocks];
        for (var y = _top; y < _height - _bottom; y++)
            for (var b = 0; b < _blocks; b++)
            {
                // 단색 부분(빈 여백)은 같은 자리에 그대로여도 고정된 것으로 보지 않는다 — 그렇게 보면 나중에 나타나는 머리글 같은 것이 그 위에 그려진다
                var (from, length) = BlockBytes(y, b);
                var block = frame.AsSpan(from, length);
                fixedBlock[y * _blocks + b] = !Uniform(block) && block.SequenceEqual(prev.AsSpan(from, length));
            }

        // 이번에 고정된 것으로 드러난 곳은 앞 화면에서도 고정된 것이었다 — 그때 그린 곳을 바꿔 그릴 수 있게 표시
        for (var y = _top; y < _height - _bottom; y++)
        {
            var outPrev = y + _prevOffset;
            if (outPrev >= _rows.Count) break;
            var state = _state[outPrev];
            for (var b = 0; b < _blocks; b++)
                if (fixedBlock[y * _blocks + b] && state[b] == FromContent) state[b] = FromFixed;
        }

        var limit = _maxHeight - _bottom;
        var limited = false;
        for (var y = _top; y < _height - _bottom; y++)
        {
            var outRow = y + offset;
            if (outRow >= limit) { limited = true; break; }
            if (outRow > _rows.Count) break; // 이어지지 않음 (일어나지 않아야 함)
            if (outRow == _rows.Count)
            {
                _rows.Add(new byte[_rowBytes]);
                _state.Add(new byte[_blocks]);
            }
            var row = _rows[outRow];
            var state = _state[outRow];
            for (var b = 0; b < _blocks; b++)
            {
                var isFixed = fixedBlock[y * _blocks + b];
                // 진짜 내용으로 그린 곳은 그대로, 고정된 것으로 그린 곳은 진짜 내용이 보일 때만 바꾼다
                if (state[b] == FromContent || (state[b] == FromFixed && isFixed)) continue;
                var (from, length) = BlockBytes(y, b);
                frame.AsSpan(from, length).CopyTo(row.AsSpan(from - y * _rowBytes));
                state[b] = isFixed ? FromFixed : FromContent;
            }
        }
        _prevOffset = offset;
        return limited || _rows.Count + _bottom >= _maxHeight;
    }

    private static bool Uniform(ReadOnlySpan<byte> pixels)
    {
        for (var i = 4; i < pixels.Length; i++)
            if (pixels[i] != pixels[i - 4]) return false;
        return true;
    }

    private (int From, int Length) BlockBytes(int y, int b)
    {
        var x0 = b * Block;
        var x1 = Math.Min(_width, x0 + Block);
        return (y * _rowBytes + x0 * 4, (x1 - x0) * 4);
    }

    /// <summary>
    /// 이번 화면 구간의 i번째 줄이 앞 화면 구간의 i+s번째 줄과 같아지는 s를 찾는다 (고정된 칸·빈 칸은 뺌).
    /// 가장 잘 맞는 s(같으면 예상한 이동량에 가까운 것)가 40% 이상 맞고, 멀리 떨어진 다른 후보보다 뚜렷이 잘 맞아야 한다.
    /// </summary>
    private int FindShift(ulong[] hash, bool[] info, bool[] fixedCell, int band, int expected, int movable)
    {
        var minOverlap = Math.Max(4, band / 10);
        var ratios = new double[band + 1];
        int bestShift = 0, bestMatches = 0;
        double bestRatio = 0;
        for (var s = 1; s <= band - minOverlap; s++)
        {
            var (considered, matched) = Score(hash, info, fixedCell, s);
            if (considered < MinConsidered(band, s, movable)) continue;
            var ratio = ratios[s] = (double)matched / considered;
            var better = bestShift == 0 || ratio > bestRatio + 1e-9
                || (Math.Abs(ratio - bestRatio) < 1e-9 && (Math.Abs(s - expected) < Math.Abs(bestShift - expected)
                    || (Math.Abs(s - expected) == Math.Abs(bestShift - expected) && matched > bestMatches)));
            if (better)
            {
                bestRatio = ratio;
                bestMatches = matched;
                bestShift = s;
            }
        }
        if (bestShift == 0 || bestRatio < MinMatchRatio) return 0;
        // 똑같이 잘 맞는 후보(되풀이되는 무늬)는 경쟁자로 보지 않는다 — 예상한 이동량으로 이미 골랐다
        var rival = 0.0;
        for (var s = 1; s < ratios.Length; s++)
            if (Math.Abs(s - bestShift) > 3 && ratios[s] < bestRatio - 1e-9) rival = Math.Max(rival, ratios[s]);
        // 뚜렷이 가장 잘 맞거나(다른 후보의 두 배), 거의 다 맞아 다른 후보보다 어긋난 곳이 훨씬 적으면(1/3 이하) 받아들인다 —
        // 파일 목록처럼 줄마다 이름만 다르면 한 줄 어긋난 이동량도 꽤 맞기 때문
        return rival * Dominance <= bestRatio || 1 - rival >= 3 * (1 - bestRatio) ? bestShift : 0;
    }

    private (int Considered, int Matched) Score(ulong[] hash, bool[] info, bool[] fixedCell, int s)
    {
        var prev = _prevHash!;
        var prevInfo = _prevInfo!;
        int considered = 0, matched = 0;
        for (var y = _top; y < _height - _bottom - s; y++)
        {
            var cur = y * _segments;
            var old = (y + s) * _segments;
            for (var k = 0; k < _segments; k++)
            {
                if (fixedCell[cur + k] || fixedCell[old + k]) continue;
                if (!info[cur + k] && !prevInfo[old + k]) continue;
                considered++;
                if (hash[cur + k] == prev[old + k]) matched++;
            }
        }
        return (considered, matched);
    }

    /// <summary>판정에 필요한 최소 칸 수: 움직인 칸 가운데 겹치는 부분의 5% (적어도 6칸).</summary>
    private static int MinConsidered(int band, int s, int movable) => Math.Max(6, (int)((long)movable * (band - s) / band / 20));

    private bool RowEqual(ulong[] a, ulong[] b, int y)
    {
        for (var k = 0; k < _segments; k++)
            if (a[y * _segments + k] != b[y * _segments + k]) return false;
        return true;
    }

    private bool RowInformative(bool[] info, int y)
    {
        for (var k = 0; k < _segments; k++)
            if (info[y * _segments + k]) return true;
        return false;
    }

    private byte[] Filled(byte value)
    {
        var a = new byte[_blocks];
        Array.Fill(a, value);
        return a;
    }

    private void Remember(ulong[] hash, bool[] info)
    {
        _prevHash = hash;
        _prevInfo = info;
    }

    /// <summary>칸마다 해시(FNV-1a)와, 단색이 아닌 칸인지.</summary>
    private (ulong[] Hash, bool[] Info) HashCells(byte[] frame)
    {
        var hash = new ulong[_height * _segments];
        var info = new bool[_height * _segments];
        for (var y = 0; y < _height; y++)
        {
            var rowStart = y * _rowBytes;
            for (var k = 0; k < _segments; k++)
            {
                var start = rowStart + _segStart[k] * 4;
                var end = rowStart + _segStart[k + 1] * 4;
                var h = 14695981039346656037UL;
                var informative = false;
                for (var i = start; i < end; i++)
                {
                    var b = frame[i];
                    h = (h ^ b) * 1099511628211UL;
                    if (!informative && i >= start + 4 && b != frame[i - 4]) informative = true;
                }
                hash[y * _segments + k] = h;
                info[y * _segments + k] = informative;
            }
        }
        return (hash, info);
    }
}
