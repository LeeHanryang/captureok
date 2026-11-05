using CaptureOk.Core.Geometry;

namespace CaptureOk.Core.Selection;

public enum SelectionPhase { Hovering, Dragging, Selected, Confirmed, Cancelled }

public enum SelectionKey { Left, Up, Right, Down, Enter, Escape, RightButton }

/// <summary>
/// 정지 화면 위 범위 선택 상태 기계. 좌표는 물리 픽셀 가상 화면 기준.
/// fixedSize가 있으면 커서를 중심으로 한 고정 크기 사각형을 클릭 한 번으로 확정한다.
/// confirmOnRelease가 true면 드래그를 놓는 순간 확정한다 (기본 캡처 흐름). false면 놓은 뒤 조정하고 Enter·더블클릭으로 확정한다.
/// </summary>
public sealed class RegionSelection(ScreenLayout layout, PixelSize? fixedSize = null, bool confirmOnRelease = false)
{
    private const int SmallStep = 1;
    private const int LargeStep = 5;

    private (int X, int Y) _anchor;
    private int _farthest; // 누른 곳에서 가장 멀리 간 거리 (한 번 끌면 제자리로 와도 드래그)
    private bool _moving;
    private (int X, int Y) _moveFrom;
    private PixelRect _moveStart;

    public SelectionPhase Phase { get; private set; } = SelectionPhase.Hovering;
    public PixelRect? Rect { get; private set; }
    public (int X, int Y) Cursor { get; private set; }
    public bool IsFinished => Phase is SelectionPhase.Confirmed or SelectionPhase.Cancelled;

    public void MouseMove(int x, int y, bool shift)
    {
        if (IsFinished) return;
        Cursor = (x, y);
        if (Phase == SelectionPhase.Dragging) _farthest = Math.Max(_farthest, Math.Max(Math.Abs(x - _anchor.X), Math.Abs(y - _anchor.Y)));
        if (_moving)
        {
            Rect = ShiftInside(_moveStart with { X = _moveStart.X + x - _moveFrom.X, Y = _moveStart.Y + y - _moveFrom.Y });
            return;
        }
        Update(shift);
    }

    public void MouseDown(int x, int y)
    {
        if (IsFinished) return;
        Cursor = (x, y);
        if (Phase == SelectionPhase.Selected && Rect is { } current && current.Contains(x, y))
        {
            // 선택 영역 안을 누르면 영역 이동 (더블클릭 확정의 첫 클릭도 여기로 온다)
            _moving = true;
            _moveFrom = (x, y);
            _moveStart = current;
            return;
        }
        if (fixedSize is not null)
        {
            Update(shift: false);
            Phase = SelectionPhase.Confirmed;
            return;
        }
        _anchor = (x, y);
        _farthest = 0;
        Phase = SelectionPhase.Dragging;
        Update(shift: false);
    }

    /// <summary>누른 곳에서 가로·세로 이만큼까지 움직였다 놓으면 드래그가 아니라 클릭 (손떨림으로 작은 영역이 잡히지 않게).</summary>
    public const int ClickTolerance = 3;

    /// <returns>클릭이었으면 true (선택 없이 Hovering으로 돌아감 — 호출자가 강조된 창·요소로 확정할 수 있다).</returns>
    public bool MouseUp(int x, int y, bool shift)
    {
        if (_moving)
        {
            MouseMove(x, y, shift);
            _moving = false;
            return false;
        }
        if (Phase != SelectionPhase.Dragging) return false;
        Cursor = (x, y);
        Update(shift);
        _farthest = Math.Max(_farthest, Math.Max(Math.Abs(x - _anchor.X), Math.Abs(y - _anchor.Y)));
        if (_farthest <= ClickTolerance)
        {
            Phase = SelectionPhase.Hovering;
            Rect = null;
            return true;
        }
        Phase = confirmOnRelease ? SelectionPhase.Confirmed : SelectionPhase.Selected;
        return false;
    }

    /// <summary>
    /// 버튼을 누른 채 끝내지 않고 지금 끈 범위를 꺼낸다 (드래그 중 우클릭 메뉴). 끄던 상태는 고르기 전으로 돌아가, 나중에 버튼을 떼도 확정되지 않는다.
    /// 거의 끌지 않았으면(클릭과 같음) 범위 없이 돌아간다. 끄는 중이 아니면 아무것도 바꾸지 않고 null.
    /// </summary>
    public PixelRect? DetachDrag()
    {
        if (Phase != SelectionPhase.Dragging) return null;
        var range = _farthest > ClickTolerance ? Rect : null;
        Phase = SelectionPhase.Hovering;
        Rect = null;
        return range;
    }

    public void DoubleClick()
    {
        if (Phase == SelectionPhase.Selected) Phase = SelectionPhase.Confirmed;
    }

    /// <summary>바깥에서 정한 영역(예: 클릭한 창)으로 바로 확정한다.</summary>
    public void Confirm(PixelRect rect)
    {
        if (IsFinished) return;
        Rect = layout.Clamp(rect);
        Phase = SelectionPhase.Confirmed;
    }

    /// <returns>true면 호출자가 실제 커서를 <see cref="Cursor"/>로 옮겨야 한다.</returns>
    public bool Key(SelectionKey key, bool ctrl, bool alt)
    {
        if (IsFinished) return false;
        var step = ctrl ? LargeStep : SmallStep;

        switch (key)
        {
            case SelectionKey.Escape:
                Phase = SelectionPhase.Cancelled;
                return false;
            case SelectionKey.RightButton:
                if (Phase == SelectionPhase.Selected)
                {
                    Phase = SelectionPhase.Hovering;
                    Rect = null;
                }
                else
                {
                    Phase = SelectionPhase.Cancelled;
                }
                return false;
            case SelectionKey.Enter:
                if (Phase == SelectionPhase.Selected || (fixedSize is not null && Rect is not null))
                    Phase = SelectionPhase.Confirmed;
                return false;
        }

        var (dx, dy) = key switch
        {
            SelectionKey.Left => (-1, 0),
            SelectionKey.Right => (1, 0),
            SelectionKey.Up => (0, -1),
            _ => (0, 1),
        };

        if (Phase == SelectionPhase.Selected && Rect is { } r)
        {
            Rect = alt
                ? layout.Clamp(r with { Width = Math.Max(1, r.Width + dx), Height = Math.Max(1, r.Height + dy) })
                : ShiftInside(r with { X = r.X + dx * step, Y = r.Y + dy * step });
            return false;
        }

        var vb = layout.VirtualBounds;
        Cursor = (Math.Clamp(Cursor.X + dx * step, vb.X, vb.Right - 1), Math.Clamp(Cursor.Y + dy * step, vb.Y, vb.Bottom - 1));
        Update(shift: false);
        return true;
    }

    private void Update(bool shift)
    {
        if (fixedSize is { } size && Phase == SelectionPhase.Hovering)
        {
            Rect = ShiftInside(new PixelRect(Cursor.X - size.Width / 2, Cursor.Y - size.Height / 2, size.Width, size.Height));
            return;
        }
        if (Phase != SelectionPhase.Dragging) return;

        var (x2, y2) = Cursor;
        if (shift)
        {
            var side = Math.Min(Math.Abs(x2 - _anchor.X), Math.Abs(y2 - _anchor.Y));
            x2 = _anchor.X + Math.Sign(x2 - _anchor.X) * side;
            y2 = _anchor.Y + Math.Sign(y2 - _anchor.Y) * side;
        }
        Rect = layout.Clamp(PixelRect.FromPoints(_anchor.X, _anchor.Y, x2, y2));
    }

    /// <summary>크기를 유지한 채 가상 화면 안으로 밀어 넣는다 (화면보다 크면 잘라 낸다).</summary>
    private PixelRect ShiftInside(PixelRect r)
    {
        var vb = layout.VirtualBounds;
        var x = Math.Clamp(r.X, vb.X, Math.Max(vb.X, vb.Right - r.Width));
        var y = Math.Clamp(r.Y, vb.Y, Math.Max(vb.Y, vb.Bottom - r.Height));
        return layout.Clamp(r with { X = x, Y = y });
    }
}
