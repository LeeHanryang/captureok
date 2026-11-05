using CaptureOk.Core.Geometry;

namespace CaptureOk.Core.Windows;

/// <summary>요소 사슬의 한 단계: 경계와, 그 요소가 세로로 스크롤되는지.</summary>
public readonly record struct ElementLevel(PixelRect Rect, bool Scrollable);

/// <summary>
/// 커서를 포함하는 화면 요소 경계 사슬(바깥 창 → … → 가장 안쪽). 처음엔 쓸 만한 가장 안쪽 요소를 고르고,
/// 휠로 바깥(Wider)·안쪽(Narrower)으로 옮긴다. 같은 경계가 이어지면 한 단계로, 너무 작은 요소는 뺀다.
/// 스크롤 캡처(preferScrollable)는 실제로 스크롤되는 가장 안쪽 요소부터, 없으면 창 전체부터 고른다.
/// </summary>
public sealed class ElementChain
{
    public const int MinSide = 4;

    private readonly List<ElementLevel> _levels = [];
    private int _index;

    public ElementChain(IReadOnlyList<PixelRect> outerToInner)
        : this(outerToInner.Select(r => new ElementLevel(r, false)).ToList())
    {
    }

    public ElementChain(IReadOnlyList<ElementLevel> outerToInner, bool preferScrollable = false)
    {
        foreach (var level in outerToInner)
        {
            var r = level.Rect;
            if (r.Width < MinSide || r.Height < MinSide) continue;
            if (_levels.Count > 0 && _levels[^1].Rect == r)
            {
                // 같은 경계의 바깥 틀과 안쪽 목록은 한 단계 — 둘 중 하나라도 스크롤되면 스크롤되는 것
                if (level.Scrollable) _levels[^1] = _levels[^1] with { Scrollable = true };
                continue;
            }
            _levels.Add(level);
        }
        _index = _levels.Count - 1;
        if (preferScrollable && _levels.Count > 0) _index = Math.Max(0, _levels.FindLastIndex(l => l.Scrollable));
    }

    public PixelRect? Current => _index >= 0 ? _levels[_index].Rect : null;

    /// <summary>휠로 단계를 직접 골랐는지.</summary>
    public bool PickedWithWheel { get; private set; }

    /// <summary>스크롤되는 요소가 하나라도 있는지.</summary>
    public bool HasScrollable => _levels.Any(l => l.Scrollable);

    /// <summary>쓸 수 있는 가장 안쪽 요소 (휠로 고른 단계와 상관없이).</summary>
    public PixelRect? Innermost => _levels.Count > 0 ? _levels[^1].Rect : null;

    /// <summary>
    /// 지금 단계에서 시작해 (x, y)를 포함하는 단계까지 바깥으로 올라간 요소. 새 감지 결과가 오기 전에 커서가 움직였을 때
    /// 이미 벗어난 안쪽 요소를 강조·캡처하지 않게 한다. 어느 단계에도 없으면 null.
    /// </summary>
    public PixelRect? CurrentContaining(int x, int y)
    {
        for (var i = _index; i >= 0; i--)
            if (_levels[i].Rect.Contains(x, y)) return _levels[i].Rect;
        return null;
    }

    /// <summary>
    /// 스크롤 캡처할 곳: 휠로 직접 고른 단계가 있으면 그 단계, 아니면 (x, y)를 포함하는 가장 안쪽 스크롤 요소, 그것도 없으면 창 전체.
    /// (x, y)가 사슬 어디에도 없으면 null.
    /// </summary>
    public PixelRect? ScrollTargetAt(int x, int y)
    {
        if (PickedWithWheel) return CurrentContaining(x, y);
        for (var i = _levels.Count - 1; i >= 0; i--)
            if (_levels[i].Scrollable && _levels[i].Rect.Contains(x, y)) return _levels[i].Rect;
        return _levels.Count > 0 && _levels[0].Rect.Contains(x, y) ? _levels[0].Rect : null;
    }

    public bool Wider()
    {
        if (_index <= 0) return false;
        _index--;
        PickedWithWheel = true;
        return true;
    }

    public bool Narrower()
    {
        if (_index < 0 || _index >= _levels.Count - 1) return false;
        _index++;
        PickedWithWheel = true;
        return true;
    }
}
