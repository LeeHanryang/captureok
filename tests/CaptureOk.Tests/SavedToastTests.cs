using System.Xml.Linq;
using CaptureOk.Core.Files;

namespace CaptureOk.Tests;

public class SavedToastTests
{
    private static XDocument Parse(string title, string body) => XDocument.Parse(SavedToast.Xml(title, body));

    [Fact(DisplayName = "toast-buttons: 알림에 열기·폴더 열기 버튼이 있고, 알림을 누르면 파일을 연다")]
    public void HasOpenAndFolderButtons()
    {
        var toast = Parse("GIF를 저장했습니다", "a.gif (3초, 1.2MB)").Root!;

        Assert.Equal("toast", toast.Name.LocalName);
        Assert.Equal(SavedToast.OpenArgument, (string?)toast.Attribute("launch"));
        var actions = toast.Descendants("action").Select(a => ((string?)a.Attribute("content"), (string?)a.Attribute("arguments"))).ToList();
        Assert.Equal([("열기", SavedToast.OpenArgument), ("폴더 열기", SavedToast.FolderArgument)], actions);
        Assert.All(toast.Descendants("action"), a => Assert.Equal("foreground", (string?)a.Attribute("activationType")));
    }

    [Fact(DisplayName = "toast-text: 제목과 내용 줄이 그대로 들어간다")]
    public void TextLines()
    {
        var texts = Parse("저장했습니다", "a.png\n최대 30초에 닿아 멈췄습니다").Descendants("text").Select(t => t.Value).ToList();

        Assert.Equal(["저장했습니다", "a.png", "최대 30초에 닿아 멈췄습니다"], texts);
    }

    [Fact(DisplayName = "toast-escape: 파일 이름의 특수 문자가 알림 형식을 깨지 않는다")]
    public void EscapesSpecialCharacters()
    {
        var texts = Parse("저장 <완료> & \"끝\"", "a&b<c>'d'.png").Descendants("text").Select(t => t.Value).ToList();

        Assert.Equal(["저장 <완료> & \"끝\"", "a&b<c>'d'.png"], texts);
    }

    [Theory(DisplayName = "toast-action: 누른 곳에 따라 할 일")]
    [InlineData(SavedToast.OpenArgument, SavedToastAction.Open)]
    [InlineData(SavedToast.FolderArgument, SavedToastAction.Folder)]
    [InlineData("", SavedToastAction.None)]
    [InlineData("other", SavedToastAction.None)]
    public void ActionOfArgument(string argument, SavedToastAction expected) =>
        Assert.Equal(expected, SavedToast.ActionOf(argument));
}
