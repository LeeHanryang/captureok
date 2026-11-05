namespace CaptureOk.Core.Imaging;

/// <summary>
/// 한 프레임의 색을 256색 이하로 줄인다 (median cut). 색을 채널당 6비트 칸으로 묶어 세므로 빠르고,
/// 칸의 대표색은 실제 픽셀의 평균이라 색이 적은 화면(프로그램 창 등)은 원래 색 그대로 남는다.
/// </summary>
internal sealed class ColorQuantizer
{
    private const int CellCount = 1 << 18;

    private readonly int[] _counts = new int[CellCount];
    private readonly long[] _sumR = new long[CellCount];
    private readonly long[] _sumG = new long[CellCount];
    private readonly long[] _sumB = new long[CellCount];
    private readonly byte[] _map = new byte[CellCount];
    private readonly int[] _cells = new int[CellCount];
    private readonly int[] _sortKeys = new int[CellCount];
    private int _cellCount;

    private readonly int[] _boxStart = new int[256];
    private readonly int[] _boxEnd = new int[256];
    private readonly long[] _boxScore = new long[256];
    private readonly int[] _boxAxis = new int[256];

    public void Reset()
    {
        for (var i = 0; i < _cellCount; i++)
        {
            var cell = _cells[i];
            _counts[cell] = 0;
            _sumR[cell] = _sumG[cell] = _sumB[cell] = 0;
        }
        _cellCount = 0;
    }

    /// <param name="color">0xAARRGGBB (알파 무시)</param>
    public void Add(uint color)
    {
        var cell = Cell(color);
        if (_counts[cell]++ == 0) _cells[_cellCount++] = cell;
        _sumR[cell] += color >> 16 & 0xFF;
        _sumG[cell] += color >> 8 & 0xFF;
        _sumB[cell] += color & 0xFF;
    }

    /// <summary>색을 maxColors개 이하로 나눠 palette(RGB 순서)에 쓰고 색 수를 돌려준다. 아무 색도 없으면 0.</summary>
    public int Build(int maxColors, byte[] palette)
    {
        if (_cellCount == 0) return 0;
        var boxes = 1;
        SetBox(0, 0, _cellCount);
        while (boxes < maxColors)
        {
            var best = -1;
            long bestScore = 0;
            for (var i = 0; i < boxes; i++)
            {
                if (_boxScore[i] > bestScore)
                {
                    bestScore = _boxScore[i];
                    best = i;
                }
            }
            if (best < 0) break; // 모든 상자가 한 칸뿐

            var split = Split(best);
            var end = _boxEnd[best];
            SetBox(best, _boxStart[best], split);
            SetBox(boxes, split, end);
            boxes++;
        }

        for (var box = 0; box < boxes; box++)
        {
            long r = 0, g = 0, b = 0, count = 0;
            for (var i = _boxStart[box]; i < _boxEnd[box]; i++)
            {
                var cell = _cells[i];
                r += _sumR[cell];
                g += _sumG[cell];
                b += _sumB[cell];
                count += _counts[cell];
                _map[cell] = (byte)box;
            }
            palette[box * 3] = (byte)((r + count / 2) / count);
            palette[box * 3 + 1] = (byte)((g + count / 2) / count);
            palette[box * 3 + 2] = (byte)((b + count / 2) / count);
        }
        return boxes;
    }

    /// <summary>Build 뒤에, Add로 넣었던 색의 팔레트 번호.</summary>
    public byte IndexOf(uint color) => _map[Cell(color)];

    private static int Cell(uint color) =>
        (int)((color >> 18 & 0x3F) << 12 | (color >> 10 & 0x3F) << 6 | (color >> 2 & 0x3F));

    private static int Channel(int cell, int axis) => cell >> (axis * 6) & 0x3F;

    /// <summary>상자의 범위를 정하고, 다음에 나눌 상자를 고를 점수(픽셀 수 × 가장 넓은 채널 폭)를 매긴다.</summary>
    private void SetBox(int box, int start, int end)
    {
        _boxStart[box] = start;
        _boxEnd[box] = end;
        if (end - start < 2)
        {
            _boxScore[box] = 0;
            return;
        }

        long pixels = 0;
        var bestRange = -1;
        for (var axis = 0; axis < 3; axis++)
        {
            int min = int.MaxValue, max = int.MinValue;
            for (var i = start; i < end; i++)
            {
                var value = Channel(_cells[i], axis);
                min = Math.Min(min, value);
                max = Math.Max(max, value);
            }
            if (max - min > bestRange)
            {
                bestRange = max - min;
                _boxAxis[box] = axis;
            }
        }
        for (var i = start; i < end; i++) pixels += _counts[_cells[i]];
        _boxScore[box] = pixels * bestRange;
    }

    /// <summary>가장 넓은 채널로 줄을 세워 픽셀 수가 반씩 되는 자리를 돌려준다. 양쪽 모두 한 칸 이상 남는다.</summary>
    private int Split(int box)
    {
        int start = _boxStart[box], end = _boxEnd[box], axis = _boxAxis[box];
        long total = 0;
        for (var i = start; i < end; i++)
        {
            _sortKeys[i] = Channel(_cells[i], axis);
            total += _counts[_cells[i]];
        }
        Array.Sort(_sortKeys, _cells, start, end - start);

        long seen = 0;
        for (var i = start; i < end - 1; i++)
        {
            seen += _counts[_cells[i]];
            if (seen * 2 >= total) return i + 1;
        }
        return end - 1;
    }
}
