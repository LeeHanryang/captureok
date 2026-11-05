using CaptureOk.Core.Selection;

namespace CaptureOk.Tests;

public class PrecisionMoverTests
{
    [Fact(DisplayName = "slow-move-sixteenth")]
    public void SlowMoveSixteenth()
    {
        var mover = new PrecisionMover();
        mover.Reset(100, 100);

        Assert.Equal((100, 100), mover.Move(108, 100, slow: true)); // 8px로는 아직 안 움직임
        Assert.Equal((101, 100), mover.Move(108, 100, slow: true));
        // 커서는 (101,100)으로 옮겨졌고, 사용자가 이번에는 한 번에 16px 움직임
        Assert.Equal((102, 100), mover.Move(117, 100, slow: true));
    }

    [Fact(DisplayName = "slow-move-accumulates-fraction")]
    public void SlowMoveAccumulatesFraction()
    {
        var mover = new PrecisionMover();
        mover.Reset(100, 100);

        Assert.Equal((100, 100), mover.Move(104, 100, slow: true));
        Assert.Equal((100, 100), mover.Move(104, 100, slow: true));
        Assert.Equal((100, 100), mover.Move(104, 100, slow: true));
        Assert.Equal((101, 100), mover.Move(104, 100, slow: true)); // 4px × 4번 = 16px → 1px
    }

    [Fact(DisplayName = "normal-move-passes-through")]
    public void NormalMovePassesThrough()
    {
        var mover = new PrecisionMover();
        mover.Reset(100, 100);

        Assert.Equal((140, 90), mover.Move(140, 90, slow: false));
    }
}
