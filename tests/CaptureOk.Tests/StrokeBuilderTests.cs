using CaptureOk.Core.Editing;
using CaptureOk.Core.Geometry;

namespace CaptureOk.Tests;

public class StrokeBuilderTests
{
    private static readonly PointD Start = new(10, 20);

    private static StrokeBuilder New() => new(Start, minSpacing: 2);

    [Fact(DisplayName = "free-stroke-collects-points")]
    public void FreeStrokeCollectsPoints()
    {
        var stroke = New();
        Assert.True(stroke.Add(new PointD(20, 25), shift: false, ctrl: false));
        Assert.True(stroke.Add(new PointD(30, 22), shift: false, ctrl: false));
        Assert.Equal([Start, new PointD(20, 25), new PointD(30, 22)], stroke.Points);
    }

    [Fact(DisplayName = "free-stroke-skips-points-too-close")]
    public void FreeStrokeSkipsPointsTooClose()
    {
        var stroke = New();
        Assert.False(stroke.Add(new PointD(11, 21), shift: false, ctrl: false));
        Assert.Equal([Start], stroke.Points);
    }

    [Fact(DisplayName = "shift-makes-perfect-line")]
    public void ShiftMakesPerfectLine()
    {
        var stroke = New();
        Assert.True(stroke.Add(new PointD(200, 35), shift: true, ctrl: false));
        Assert.Equal([Start, new PointD(200, 20)], stroke.Points);
    }

    [Fact(DisplayName = "ctrl-makes-free-angle-line")]
    public void CtrlMakesFreeAngleLine()
    {
        var stroke = New();
        Assert.True(stroke.Add(new PointD(200, 35), shift: false, ctrl: true));
        Assert.Equal([Start, new PointD(200, 35)], stroke.Points);
    }

    [Fact(DisplayName = "releasing-shift-before-the-button-keeps-the-line-perfect")]
    public void ReleasingShiftBeforeTheButtonKeepsTheLinePerfect()
    {
        var stroke = New();
        stroke.Add(new PointD(200, 35), shift: true, ctrl: false);
        stroke.Add(new PointD(205, 36), shift: false, ctrl: false); // Shift를 먼저 뗌 — 끝에 갈고리가 생기면 안 된다
        Assert.Equal([Start, new PointD(205, 20)], stroke.Points);
    }

    [Fact(DisplayName = "releasing-ctrl-keeps-the-straight-line")]
    public void ReleasingCtrlKeepsTheStraightLine()
    {
        var stroke = New();
        stroke.Add(new PointD(100, 50), shift: false, ctrl: true);
        stroke.Add(new PointD(120, 60), shift: false, ctrl: false);
        Assert.Equal([Start, new PointD(120, 60)], stroke.Points);
    }

    [Fact(DisplayName = "switching-modifier-switches-line-kind")]
    public void SwitchingModifierSwitchesLineKind()
    {
        var stroke = New();
        stroke.Add(new PointD(100, 50), shift: false, ctrl: true);
        stroke.Add(new PointD(100, 50), shift: true, ctrl: false);
        Assert.Equal([Start, new PointD(100, 20)], stroke.Points);
        stroke.Add(new PointD(100, 50), shift: false, ctrl: true);
        Assert.Equal([Start, new PointD(100, 50)], stroke.Points);
    }

    [Fact(DisplayName = "modifier-during-free-stroke-turns-it-into-a-line")]
    public void ModifierDuringFreeStrokeTurnsItIntoALine()
    {
        var stroke = New();
        stroke.Add(new PointD(20, 40), shift: false, ctrl: false);
        stroke.Add(new PointD(30, 90), shift: false, ctrl: false);
        stroke.Add(new PointD(40, 95), shift: true, ctrl: false);
        Assert.Equal([Start, new PointD(10, 95)], stroke.Points);
    }
}
