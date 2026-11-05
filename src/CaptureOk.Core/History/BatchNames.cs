using CaptureOk.Core.Editing;
using CaptureOk.Core.Files;

namespace CaptureOk.Core.History;

/// <summary>"모두 저장"의 파일 이름 정하기.</summary>
public static class BatchNames
{
    /// <summary>
    /// 문서마다 찍은 시각으로 이름을 짓는다. 같은 초에 찍은 캡처끼리도 겹치지 않도록,
    /// 이번에 이미 정한 이름도 있는 파일로 친다.
    /// </summary>
    public static IReadOnlyList<(CaptureDocument Doc, string Path)> Plan(IEnumerable<CaptureDocument> docs, string folder, string template,
        string extension, Func<string, bool> exists)
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var plan = new List<(CaptureDocument, string)>();
        foreach (var doc in docs)
        {
            var path = FileNamer.Next(folder, template, doc.CreatedAt, extension, p => taken.Contains(p) || exists(p));
            taken.Add(path);
            plan.Add((doc, path));
        }
        return plan;
    }
}
