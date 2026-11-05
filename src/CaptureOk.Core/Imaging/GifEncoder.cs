using System.Runtime.InteropServices;
using System.Text;

namespace CaptureOk.Core.Imaging;

/// <summary>
/// 화면을 차례로 받아 무한 반복하는 GIF(GIF89a)로 바로 써 나간다 — 긴 녹화도 메모리에 쌓이지 않게.
/// 앞 화면과 같으면 프레임을 늘리지 않고, 다르면 바뀐 칸만 쓴다. 바뀌지 않은 픽셀은 투명으로 두어 프레임마다 팔레트가 달라도 색이 튀지 않는다.
/// 프레임의 지연 시간은 다음 화면이 와야 알 수 있으므로 한 장을 들고 있다가 쓴다.
/// </summary>
public sealed class GifEncoder
{
    private const int MaxSide = 65535;
    private const int MinDelayCs = 2; // 이보다 짧으면 해독기 대부분이 지연을 무시하고 느리게 보여 준다
    private const int MaxLzwCode = 4095;
    private const int HashBits = 13;

    private readonly Stream _output;
    private readonly int _width, _height;
    private readonly uint[] _incoming;
    private uint[] _pending, _canvas;
    private readonly byte[] _indices;
    private readonly ColorQuantizer _quantizer = new();
    private readonly byte[] _palette = new byte[256 * 3];

    private bool _hasPending;
    private bool _wroteFrame;
    private long _writtenCs;

    private readonly int[] _hashKeys = new int[1 << HashBits];
    private readonly short[] _hashCodes = new short[1 << HashBits];
    private readonly byte[] _block = new byte[255];
    private int _blockLength;
    private uint _bitBuffer;
    private int _bitCount;

    public GifEncoder(Stream output, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, MaxSide);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, MaxSide);
        _output = output;
        _width = width;
        _height = height;
        _incoming = new uint[width * height];
        _pending = new uint[width * height];
        _canvas = new uint[width * height];
        _indices = new byte[width * height];
        WriteHeader();
    }

    /// <summary>화면을 쓴 프레임 수 (아직 들고 있는 한 장은 세지 않음).</summary>
    public int FramesWritten { get; private set; }

    /// <summary>timeMs(녹화 시작부터)에 찍은 화면. 픽셀은 BGRA(위에서 아래로)이고 알파는 무시한다.</summary>
    public void Add(ReadOnlySpan<byte> bgra, long timeMs)
    {
        if (bgra.Length != _incoming.Length * 4) throw new ArgumentException("화면 크기가 맞지 않습니다", nameof(bgra));
        var source = MemoryMarshal.Cast<byte, uint>(bgra);
        for (var i = 0; i < source.Length; i++) _incoming[i] = source[i] | 0xFF000000;

        if (_hasPending)
        {
            if (_incoming.AsSpan().SequenceEqual(_pending)) return; // 앞 화면을 더 오래 보인다
            WritePending(timeMs);
        }
        _incoming.CopyTo(_pending, 0);
        _hasPending = true;
    }

    /// <summary>마지막 화면을 endMs까지 보이게 쓰고 파일을 끝낸다.</summary>
    public void Finish(long endMs)
    {
        if (_hasPending) WritePending(endMs);
        _hasPending = false;
        _output.WriteByte(0x3B);
        _output.Flush();
    }

    private void WriteHeader()
    {
        WriteAscii("GIF89a");
        WriteUInt16(_width);
        WriteUInt16(_height);
        _output.WriteByte(0x70); // 전역 팔레트 없음 (프레임마다 자기 팔레트), 색 해상도 8비트
        _output.WriteByte(0); // 배경색
        _output.WriteByte(0); // 픽셀 비율
        // 무한 반복
        _output.Write([0x21, 0xFF, 0x0B]);
        WriteAscii("NETSCAPE2.0");
        _output.Write([0x03, 0x01, 0x00, 0x00, 0x00]);
    }

    private void WritePending(long untilMs)
    {
        var (left, top, right, bottom) = _wroteFrame ? ChangedBox() : (0, 0, _width, _height);
        int boxWidth = right - left, boxHeight = bottom - top;

        _quantizer.Reset();
        var hasUnchanged = false;
        for (var y = top; y < bottom; y++)
        {
            var row = y * _width;
            for (var x = left; x < right; x++)
            {
                var color = _pending[row + x];
                if (_wroteFrame && color == _canvas[row + x]) hasUnchanged = true;
                else _quantizer.Add(color);
            }
        }
        var colors = _quantizer.Build(_wroteFrame ? 255 : 256, _palette); // 뒤 프레임은 투명 자리를 남긴다
        var transparent = (byte)Math.Min(colors, 255);

        var p = 0;
        for (var y = top; y < bottom; y++)
        {
            var row = y * _width;
            for (var x = left; x < right; x++)
            {
                var color = _pending[row + x];
                _indices[p++] = _wroteFrame && color == _canvas[row + x] ? transparent : _quantizer.IndexOf(color);
            }
        }

        var entries = colors + (hasUnchanged ? 1 : 0);
        var tableBits = 1;
        while (1 << tableBits < entries) tableBits++;

        // 반올림한 누적 시간에 맞춰 지연을 정한다 — 프레임이 많아도 전체 길이가 밀리지 않게
        var delay = Math.Clamp(RoundToCs(untilMs) - _writtenCs, MinDelayCs, ushort.MaxValue);
        _writtenCs += delay;

        // 그래픽 제어: 다음 프레임 아래에 이 프레임을 남긴다
        _output.Write([0x21, 0xF9, 0x04, (byte)(1 << 2 | (hasUnchanged ? 1 : 0))]);
        WriteUInt16((int)delay);
        _output.Write([hasUnchanged ? transparent : (byte)0, 0x00]);

        _output.WriteByte(0x2C);
        WriteUInt16(left);
        WriteUInt16(top);
        WriteUInt16(boxWidth);
        WriteUInt16(boxHeight);
        _output.WriteByte((byte)(0x80 | (tableBits - 1)));
        _palette.AsSpan(colors * 3, ((1 << tableBits) - colors) * 3).Clear();
        _output.Write(_palette, 0, (1 << tableBits) * 3);

        WriteLzw(Math.Max(2, tableBits), boxWidth * boxHeight);

        (_canvas, _pending) = (_pending, _canvas);
        _wroteFrame = true;
        FramesWritten++;
    }

    private static long RoundToCs(long ms) => (ms + 5) / 10;

    /// <summary>앞 프레임과 달라진 칸. 같은 화면은 Add에서 걸러 오므로 비어 있지 않다.</summary>
    private (int Left, int Top, int Right, int Bottom) ChangedBox()
    {
        var canvas = _canvas.AsSpan();
        var pending = _pending.AsSpan();
        var top = 0;
        while (top < _height - 1 && Row(pending, top).SequenceEqual(Row(canvas, top))) top++;
        var bottom = _height;
        while (bottom > top + 1 && Row(pending, bottom - 1).SequenceEqual(Row(canvas, bottom - 1))) bottom--;

        int left = _width, right = 0;
        for (var y = top; y < bottom; y++)
        {
            var a = Row(pending, y);
            var b = Row(canvas, y);
            var first = a.CommonPrefixLength(b);
            if (first == _width) continue;
            left = Math.Min(left, first);
            var last = _width;
            while (last > first + 1 && a[last - 1] == b[last - 1]) last--;
            right = Math.Max(right, last);
        }
        return (left, top, right, bottom);
    }

    private Span<uint> Row(Span<uint> pixels, int y) => pixels.Slice(y * _width, _width);

    /// <summary>_indices 앞쪽 length칸을 GIF의 가변 길이 LZW로 쓴다 (255바이트 이하 블록으로 나눠서).</summary>
    private void WriteLzw(int minCodeSize, int length)
    {
        _output.WriteByte((byte)minCodeSize);
        _blockLength = 0;
        _bitBuffer = 0;
        _bitCount = 0;

        var clear = 1 << minCodeSize;
        var end = clear + 1;
        var bits = minCodeSize + 1;
        var last = end; // 마지막으로 정한 부호
        Array.Clear(_hashKeys);
        Emit(clear, bits);

        int prefix = _indices[0];
        for (var i = 1; i < length; i++)
        {
            int next = _indices[i];
            var key = (prefix << 8 | next) + 1; // 0은 빈 칸
            var slot = (int)((uint)key * 0x9E3779B1u >> (32 - HashBits));
            var found = false;
            while (_hashKeys[slot] != 0)
            {
                if (_hashKeys[slot] == key)
                {
                    prefix = _hashCodes[slot];
                    found = true;
                    break;
                }
                slot = (slot + 1) & ((1 << HashBits) - 1);
            }
            if (found) continue;

            Emit(prefix, bits);
            last++;
            _hashKeys[slot] = key;
            _hashCodes[slot] = (short)last;
            if (last >= 1 << bits) bits++;
            if (last == MaxLzwCode)
            {
                Emit(clear, bits);
                Array.Clear(_hashKeys);
                bits = minCodeSize + 1;
                last = end;
            }
            prefix = next;
        }

        Emit(prefix, bits);
        // 해독기는 이 부호를 읽고도 표에 한 칸 더 넣으므로 끝 부호의 길이를 그에 맞춘다
        if (last < MaxLzwCode && ++last >= 1 << bits) bits++;
        Emit(end, bits);

        if (_bitCount > 0) PushByte((byte)_bitBuffer);
        FlushBlock();
        _output.WriteByte(0);
    }

    private void Emit(int code, int bits)
    {
        _bitBuffer |= (uint)code << _bitCount;
        _bitCount += bits;
        while (_bitCount >= 8)
        {
            PushByte((byte)_bitBuffer);
            _bitBuffer >>= 8;
            _bitCount -= 8;
        }
    }

    private void PushByte(byte value)
    {
        _block[_blockLength++] = value;
        if (_blockLength == _block.Length) FlushBlock();
    }

    private void FlushBlock()
    {
        if (_blockLength == 0) return;
        _output.WriteByte((byte)_blockLength);
        _output.Write(_block, 0, _blockLength);
        _blockLength = 0;
    }

    private void WriteUInt16(int value)
    {
        _output.WriteByte((byte)value);
        _output.WriteByte((byte)(value >> 8));
    }

    private void WriteAscii(string text) => _output.Write(Encoding.ASCII.GetBytes(text));
}
