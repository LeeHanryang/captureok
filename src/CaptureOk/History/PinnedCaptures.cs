using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CaptureOk.Core.Editing;
using CaptureOk.Core.History;
using CaptureOk.Editor;

namespace CaptureOk.History;

/// <summary>
/// 고정 캡처를 디스크에 보관한다: 고정할 때 원본 PNG와 편집 상태를 쓰고, 고정 문서가 바뀌면 saveDelay 뒤
/// 편집 상태만 다시 쓴다 (앱이 갑자기 꺼져도 마지막 편집이 남게). 시작할 때 되살린다.
/// </summary>
internal sealed class PinnedCaptures(PinnedStore store, Func<Guid, BitmapSource?> imageOf, TimeSpan saveDelay)
{
    private readonly Dictionary<Guid, (CaptureDocument Doc, DispatcherTimer Timer, Action Handler)> _watched = [];

    public PinnedStore Store => store;

    /// <summary>
    /// 고정한다. 핀은 바로 표시하고, 큰 원본 PNG는 뒤에서 써서 화면이 멈추지 않게 한다.
    /// 그 사이의 편집도 지켜보다가 원본을 다 쓴 뒤 편집 상태를 쓴다. 쓰는 동안 고정을 풀면 폴더를 남기지 않는다.
    /// </summary>
    public async Task<PinResult> PinAsync(CaptureHistory history, CaptureDocument doc)
    {
        var result = history.SetPinned(doc.Id, true);
        if (result != PinResult.Pinned || _watched.ContainsKey(doc.Id)) return result;
        Watch(doc);
        try
        {
            var image = imageOf(doc.Id) ?? throw new IOException("원본 이미지가 없습니다");
            var folder = store.FolderOf(doc.Id);
            var basePath = store.BasePathOf(doc.Id);
            await Task.Run(() =>
            {
                Directory.CreateDirectory(folder);
                ImageExporter.SavePng(image, basePath, overwrite: true); // 캡처 이미지는 Freeze되어 있어 다른 스레드에서 읽어도 된다
            });
            if (!doc.IsPinned || !_watched.ContainsKey(doc.Id))
            {
                TryDelete(doc.Id); // 쓰는 동안 고정을 풀었거나 목록에서 지움
                return PinResult.Unpinned;
            }
            store.WriteDocument(doc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"고정 보관 실패 {doc.Id}: {ex.Message}");
            Unwatch(doc.Id);
            history.SetPinned(doc.Id, false);
            TryDelete(doc.Id);
            return PinResult.Failed;
        }
        return PinResult.Pinned;
    }

    public void Unpin(CaptureHistory history, CaptureDocument doc)
    {
        history.SetPinned(doc.Id, false);
        Forget(doc);
    }

    /// <summary>고정 해제했거나 목록에서 지운 문서의 보관 폴더를 지우고 더 지켜보지 않는다.</summary>
    public void Forget(CaptureDocument doc)
    {
        Unwatch(doc.Id);
        TryDelete(doc.Id);
    }

    /// <summary>보관한 고정 캡처를 되살린다. 원본을 못 읽거나 크기가 편집 상태와 다르면 log에 적고 건너뛴다.</summary>
    public IReadOnlyList<(CaptureDocument Doc, BitmapSource Image)> RestoreAll(Action<string> log)
    {
        var restored = new List<(CaptureDocument, BitmapSource)>();
        store.RemoveOrphans(log); // 쓰다 꺼졌거나 지우다 실패한 반쪽 폴더
        foreach (var doc in store.LoadAll(log))
        {
            BitmapSource image;
            try
            {
                using var stream = File.OpenRead(store.BasePathOf(doc.Id));
                image = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                image.Freeze();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException)
            {
                log($"고정 캡처 원본을 읽지 못함 {doc.Id}: {ex.Message}");
                continue;
            }
            if (image.PixelWidth != doc.ImageWidth || image.PixelHeight != doc.ImageHeight)
            {
                log($"고정 캡처 크기가 맞지 않음 {doc.Id}: 원본 {image.PixelWidth}×{image.PixelHeight}, 편집 상태 {doc.ImageWidth}×{doc.ImageHeight}");
                continue;
            }
            Watch(doc);
            restored.Add((doc, image));
        }
        return restored;
    }

    /// <summary>기다리는 저장을 지금 쓴다 (앱 종료 때).</summary>
    public void FlushAll()
    {
        foreach (var (doc, timer, _) in _watched.Values.ToList())
        {
            if (!timer.IsEnabled) continue;
            timer.Stop();
            Write(doc);
        }
    }

    private void Watch(CaptureDocument doc)
    {
        var timer = new DispatcherTimer { Interval = saveDelay };
        timer.Tick += (_, _) => { timer.Stop(); Write(doc); };
        Action handler = () => { timer.Stop(); timer.Start(); };
        doc.Changed += handler;
        _watched[doc.Id] = (doc, timer, handler);
    }

    private void Unwatch(Guid id)
    {
        if (!_watched.Remove(id, out var w)) return;
        w.Timer.Stop();
        w.Doc.Changed -= w.Handler;
    }

    private void Write(CaptureDocument doc)
    {
        // 원본을 아직 쓰는 중이면(고정 직후) 기다렸다가 PinAsync가 쓴다. 바깥에서 폴더를 지웠으면 되살리지 않는다.
        if (!doc.IsPinned || !store.HasBase(doc.Id)) return;
        try
        {
            store.WriteDocument(doc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"고정 캡처 편집 저장 실패 {doc.Id}: {ex.Message}");
        }
    }

    private void TryDelete(Guid id)
    {
        try
        {
            store.Delete(id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"고정 캡처 폴더 삭제 실패 {id}: {ex.Message}");
        }
    }
}
