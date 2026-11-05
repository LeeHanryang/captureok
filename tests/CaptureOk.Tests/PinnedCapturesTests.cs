using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CaptureOk.Core.Editing;
using CaptureOk.Core.Geometry;
using CaptureOk.Core.History;
using CaptureOk.History;

namespace CaptureOk.Tests;

public sealed class PinnedCapturesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "captureok-pinned-app-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    /// <summary>WPF 비트맵·타이머는 STA 스레드의 Dispatcher에서 돈다.</summary>
    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext()); // 앱처럼 await 뒤 UI 스레드로
            try { action(); }
            catch (Exception ex) { error = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Xunit.Sdk.XunitException($"STA 스레드 오류: {error}");
    }

    /// <summary>ms 동안 Dispatcher를 돌려 타이머가 불리게 한다.</summary>
    private static void Pump(int ms)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    /// <summary>Dispatcher를 돌리며 작업이 끝나기를 기다린다 (await 뒤 이어지는 코드가 이 스레드에서 돈다).</summary>
    private static T Wait<T>(Task<T> task)
    {
        var frame = new DispatcherFrame();
        task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
        if (!task.IsCompleted) Dispatcher.PushFrame(frame);
        Pump(20); // 이어지는 코드까지 마저 돌게
        return task.GetAwaiter().GetResult();
    }

    private static BitmapSource Colored(int size)
    {
        var pixels = new byte[size * size * 4];
        for (var i = 0; i < size * size; i++)
        {
            pixels[i * 4] = (byte)(i * 13);
            pixels[i * 4 + 1] = (byte)(i * 29);
            pixels[i * 4 + 2] = (byte)(255 - i);
            pixels[i * 4 + 3] = 255;
        }
        var image = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        image.Freeze();
        return image;
    }

    private static byte[] Pixels(BitmapSource image)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var buffer = new byte[image.PixelWidth * image.PixelHeight * 4];
        converted.CopyPixels(buffer, image.PixelWidth * 4, 0);
        return buffer;
    }

    private sealed record Setup(CaptureHistory History, CaptureDocument Doc, BitmapSource Image, PinnedStore Store, PinnedCaptures Pinned);

    private Setup Make(int docSize = 4, int imageSize = 4, int delayMs = 50)
    {
        var image = Colored(imageSize);
        var doc = new CaptureDocument(Guid.NewGuid(), docSize, docSize, DateTime.Now);
        var history = new CaptureHistory(30);
        history.Add(doc);
        var store = new PinnedStore(_root);
        var pinned = new PinnedCaptures(store, id => id == doc.Id ? image : null, TimeSpan.FromMilliseconds(delayMs));
        return new Setup(history, doc, image, store, pinned);
    }

    private static Annotation Box() => new ShapeAnnotation(Guid.NewGuid(), ShapeKind.Rectangle, new RectD(0, 0, 2, 2), new StrokeStyle(0xFFFF0000, 1, false), true);

    [Fact(DisplayName = "pin-writes-base-and-document")]
    public void PinWritesBaseAndDocument() => RunSta(() =>
    {
        var s = Make();
        Assert.Equal(PinResult.Pinned, Wait(s.Pinned.PinAsync(s.History, s.Doc)));
        Assert.True(File.Exists(s.Store.BasePathOf(s.Doc.Id)));
        Assert.True(File.Exists(Path.Combine(s.Store.FolderOf(s.Doc.Id), "document.json")));

        var restored = new PinnedCaptures(s.Store, _ => null, TimeSpan.FromMilliseconds(50)).RestoreAll(_ => { });
        var (doc, image) = Assert.Single(restored);
        Assert.Equal(s.Doc.Id, doc.Id);
        Assert.Equal(Pixels(s.Image), Pixels(image));
        Assert.True(image.IsFrozen);
    });

    [Fact(DisplayName = "pinned-edit-is-flushed")]
    public void PinnedEditIsFlushed() => RunSta(() =>
    {
        var s = Make();
        Wait(s.Pinned.PinAsync(s.History, s.Doc));
        s.Doc.Execute(new AddAnnotation(Box()));
        Pump(250); // 저장 지연(50ms)이 지나도록

        var restored = new PinnedCaptures(s.Store, _ => null, TimeSpan.FromMilliseconds(50)).RestoreAll(_ => { });
        Assert.Single(Assert.Single(restored).Doc.Annotations);
    });

    [Fact(DisplayName = "flush-all-writes-pending")]
    public void FlushAllWritesPending() => RunSta(() =>
    {
        var s = Make(delayMs: 60_000);
        Wait(s.Pinned.PinAsync(s.History, s.Doc));
        s.Doc.Execute(new AddAnnotation(Box()));
        s.Pinned.FlushAll();

        var restored = new PinnedCaptures(s.Store, _ => null, TimeSpan.FromMilliseconds(50)).RestoreAll(_ => { });
        Assert.Single(Assert.Single(restored).Doc.Annotations);
    });

    [Fact(DisplayName = "unpin-deletes-folder")]
    public void UnpinDeletesFolder() => RunSta(() =>
    {
        var s = Make();
        Wait(s.Pinned.PinAsync(s.History, s.Doc));
        s.Pinned.Unpin(s.History, s.Doc);

        Assert.False(Directory.Exists(s.Store.FolderOf(s.Doc.Id)));
        Assert.False(s.Doc.IsPinned);
        s.Doc.Execute(new AddAnnotation(Box()));
        Pump(150);
        Assert.False(Directory.Exists(s.Store.FolderOf(s.Doc.Id))); // 해제 뒤 편집은 다시 쓰지 않는다
    });

    [Fact(DisplayName = "restore-skips-size-mismatch")]
    public void RestoreSkipsSizeMismatch() => RunSta(() =>
    {
        var s = Make(docSize: 10, imageSize: 4);
        Wait(s.Pinned.PinAsync(s.History, s.Doc));

        var logs = new List<string>();
        var restored = new PinnedCaptures(s.Store, _ => null, TimeSpan.FromMilliseconds(50)).RestoreAll(logs.Add);
        Assert.Empty(restored);
        Assert.Single(logs);
    });

    [Fact(DisplayName = "edit-while-pinning-is-saved")]
    public void EditWhilePinningIsSaved() => RunSta(() =>
    {
        var s = Make();
        var pinning = s.Pinned.PinAsync(s.History, s.Doc); // 원본은 뒤에서 쓰는 중
        Assert.True(s.Doc.IsPinned);                         // 핀은 바로 표시
        s.Doc.Execute(new AddAnnotation(Box()));
        Assert.Equal(PinResult.Pinned, Wait(pinning));
        Pump(250);

        var restored = new PinnedCaptures(s.Store, _ => null, TimeSpan.FromMilliseconds(50)).RestoreAll(_ => { });
        Assert.Single(Assert.Single(restored).Doc.Annotations);
    });

    [Fact(DisplayName = "unpin-while-pinning-leaves-no-folder")]
    public void UnpinWhilePinningLeavesNoFolder() => RunSta(() =>
    {
        var s = Make();
        var pinning = s.Pinned.PinAsync(s.History, s.Doc);
        s.Pinned.Unpin(s.History, s.Doc);   // 원본을 쓰는 사이 고정 해제
        Wait(pinning);

        Assert.False(Directory.Exists(s.Store.FolderOf(s.Doc.Id)));
        Assert.False(s.Doc.IsPinned);
    });
}