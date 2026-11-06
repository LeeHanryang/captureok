using System.Xml.Linq;

namespace CaptureOk.Core.Files;

public enum SavedToastAction { None, Open, Folder }

/// <summary>
/// 저장 알림(Windows 알림)의 내용: 제목·내용 줄과 "열기"·"폴더 열기" 버튼. 알림 자체를 눌러도 파일을 연다.
/// 버튼은 앱 안에서 처리한다(파일 경로를 알림에 넣지 않음) — 누르면 <see cref="ActionOf"/>로 할 일을 정한다.
/// </summary>
public static class SavedToast
{
    public const string OpenArgument = "open";
    public const string FolderArgument = "folder";

    public static string Xml(string title, string body)
    {
        var binding = new XElement("binding", new XAttribute("template", "ToastGeneric"), new XElement("text", title));
        foreach (var line in body.Split('\n')) binding.Add(new XElement("text", line));
        var toast = new XElement("toast",
            new XAttribute("launch", OpenArgument),
            new XAttribute("activationType", "foreground"),
            new XElement("visual", binding),
            new XElement("actions",
                Action("열기", OpenArgument),
                Action("폴더 열기", FolderArgument)));
        return toast.ToString(SaveOptions.DisableFormatting);
    }

    public static SavedToastAction ActionOf(string argument) => argument switch
    {
        OpenArgument => SavedToastAction.Open,
        FolderArgument => SavedToastAction.Folder,
        _ => SavedToastAction.None,
    };

    private static XElement Action(string content, string argument) =>
        new("action", new XAttribute("content", content), new XAttribute("arguments", argument), new XAttribute("activationType", "foreground"));
}
