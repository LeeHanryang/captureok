namespace CaptureOk.Tray;

/// <summary>
/// 사용자 세션당 하나만 실행되도록 막는다.
/// 설치·제거 프로그램이 실행 중인 앱을 끌 수 있도록 "종료해 달라" 신호(<c>CaptureOk.exe --exit</c>)도 받는다.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    public const string DefaultName = "CaptureOk.SingleInstance";
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _exitSignal;
    private readonly RegisteredWaitHandle _exitWait;

    /// <summary>다른 프로세스가 종료를 요청했다 (스레드 풀에서 불림).</summary>
    public event Action? ExitRequested;

    private SingleInstance(Mutex mutex, EventWaitHandle exitSignal)
    {
        _mutex = mutex;
        _exitSignal = exitSignal;
        _exitWait = ThreadPool.RegisterWaitForSingleObject(exitSignal, (_, _) => ExitRequested?.Invoke(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public static SingleInstance? TryAcquire(string name = DefaultName)
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName(name), out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }
        return new SingleInstance(mutex, new EventWaitHandle(false, EventResetMode.AutoReset, ExitSignalName(name)));
    }

    /// <summary>실행 중인 앱에 종료를 요청한다. 실행 중이 아니면 false.</summary>
    public static bool SignalExit(string name = DefaultName)
    {
        if (!EventWaitHandle.TryOpenExisting(ExitSignalName(name), out var signal)) return false;
        using (signal) return signal.Set();
    }

    private static string MutexName(string name) => $@"Local\{name}";
    private static string ExitSignalName(string name) => $@"Local\{name}.Exit";

    public void Dispose()
    {
        _exitWait.Unregister(null);
        _exitSignal.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
