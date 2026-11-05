using CaptureOk.Tray;

namespace CaptureOk.Tests;

public class SingleInstanceTests
{
    private static string UniqueName() => "CaptureOkTest." + Guid.NewGuid().ToString("N");

    [Fact]
    public void SecondAcquireFailsWhileFirstIsHeld()
    {
        var name = UniqueName();
        using var first = SingleInstance.TryAcquire(name);
        Assert.NotNull(first);
        Assert.Null(SingleInstance.TryAcquire(name));
    }

    [Fact]
    public void SignalExitReachesRunningInstance()
    {
        var name = UniqueName();
        using var instance = SingleInstance.TryAcquire(name)!;
        using var raised = new ManualResetEventSlim();
        instance.ExitRequested += raised.Set;

        Assert.True(SingleInstance.SignalExit(name));
        Assert.True(raised.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void SignalExitWithoutRunningInstanceReturnsFalse() =>
        Assert.False(SingleInstance.SignalExit(UniqueName()));
}
