using System.Text.Json;
using CaptureOk.Core.Editing;

namespace CaptureOk.Core.History;

/// <summary>
/// 고정 캡처 보관 폴더: root\&lt;id&gt;\base.png(원본, 앱이 쓴다) + document.json(편집 상태).
/// 깨진 폴더는 앱 시작을 막지 않도록 건너뛴다.
/// </summary>
public sealed class PinnedStore(string root)
{
    private const string DocumentFile = "document.json";
    private const string BaseFile = "base.png";

    public string Root { get; } = root;

    public string FolderOf(Guid id) => Path.Combine(Root, id.ToString("N"));

    public string BasePathOf(Guid id) => Path.Combine(FolderOf(id), BaseFile);

    /// <summary>document.json을 임시 파일에 쓴 뒤 바꿔 넣는다 (쓰다 꺼져도 이전 내용이 남게).</summary>
    public void WriteDocument(CaptureDocument doc)
    {
        var folder = FolderOf(doc.Id);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, DocumentFile);
        var temp = path + ".tmp";
        File.WriteAllText(temp, DocumentJson.Serialize(doc));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>보관한 고정 캡처를 최신순으로 최대 MaxPinned개. 원본이 없거나 document.json이 깨진 폴더는 log에 적고 건너뛴다.</summary>
    public IReadOnlyList<CaptureDocument> LoadAll(Action<string> log)
    {
        if (!Directory.Exists(Root)) return [];
        List<string> folders;
        try
        {
            folders = Directory.EnumerateDirectories(Root).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log($"고정 캡처 폴더를 읽지 못함 ({ex.Message}): {Root}"); // 앱 시작은 막지 않는다
            return [];
        }
        var docs = new List<CaptureDocument>();
        foreach (var folder in folders)
        {
            try
            {
                if (!File.Exists(Path.Combine(folder, BaseFile))) { log($"고정 캡처 건너뜀 (원본 없음): {folder}"); continue; }
                var doc = DocumentJson.Deserialize(File.ReadAllText(Path.Combine(folder, DocumentFile)));
                if (!string.Equals(Path.GetFileName(folder), doc.Id.ToString("N"), StringComparison.OrdinalIgnoreCase))
                {
                    log($"고정 캡처 건너뜀 (폴더 이름과 id가 다름): {folder}");
                    continue;
                }
                docs.Add(doc);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
            {
                log($"고정 캡처 건너뜀 ({ex.Message}): {folder}");
            }
        }
        return docs.OrderByDescending(d => d.CreatedAt).Take(CaptureHistory.MaxPinned).ToList();
    }

    /// <summary>
    /// 앱 시작 때: 원본이나 편집 상태 중 하나가 없는 반쪽 폴더(쓰다 꺼짐, 지우다 실패함)를 지운다.
    /// 편집 상태를 읽지 못하는 폴더는 새 버전이 쓴 것일 수 있어 남겨 둔다. 캡처 폴더가 아닌 것은 건드리지 않는다.
    /// </summary>
    public void RemoveOrphans(Action<string> log)
    {
        if (!Directory.Exists(Root)) return;
        try
        {
            foreach (var folder in Directory.EnumerateDirectories(Root).ToList())
            {
                if (!Guid.TryParseExact(Path.GetFileName(folder), "N", out _)) continue;
                if (File.Exists(Path.Combine(folder, BaseFile)) && File.Exists(Path.Combine(folder, DocumentFile))) continue;
                try
                {
                    Directory.Delete(folder, recursive: true);
                    log($"반쪽 고정 캡처 폴더 정리: {folder}");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    log($"반쪽 고정 캡처 폴더를 지우지 못함 ({ex.Message}): {folder}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log($"고정 캡처 폴더를 읽지 못함 ({ex.Message}): {Root}");
        }
    }

    /// <summary>
    /// 고정 해제·삭제한 캡처의 폴더를 지운다. 없으면 아무것도 하지 않는다.
    /// 파일이 잠겨 폴더를 못 지우면 편집 상태라도 지워 다음 실행에 고정 캡처로 되살아나지 않게 하고 예외를 다시 던진다
    /// (남은 폴더는 다음 시작 때 RemoveOrphans가 정리).
    /// </summary>
    public void Delete(Guid id)
    {
        var folder = FolderOf(id);
        if (!Directory.Exists(folder)) return;
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(Path.Combine(folder, DocumentFile)); }
            catch (Exception inner) when (inner is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    /// <summary>원본(base.png)이 있는지 — 편집 상태만 다시 쓸 때, 지워진 폴더를 되살리지 않으려고 확인한다.</summary>
    public bool HasBase(Guid id) => File.Exists(BasePathOf(id));
}
