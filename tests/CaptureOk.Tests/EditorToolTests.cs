using CaptureOk.Core.Editing;

namespace CaptureOk.Tests;

public class EditorToolTests
{
    [Fact(DisplayName = "order-matches-spec")]
    public void OrderMatchesSpec()
    {
        Assert.Equal(
            [EditorTool.Select, EditorTool.Pen, EditorTool.Highlighter, EditorTool.Rectangle, EditorTool.Ellipse, EditorTool.Line,
             EditorTool.Arrow, EditorTool.Text, EditorTool.Number, EditorTool.Mosaic, EditorTool.Blur, EditorTool.Crop, EditorTool.Eraser],
            EditorTools.Order);
    }

    [Theory(DisplayName = "cursor-matches-tool")]
    [InlineData(EditorTool.Select, ToolCursor.Arrow)]
    [InlineData(EditorTool.Pen, ToolCursor.Pen)]           // 펜이면 펜 모양
    [InlineData(EditorTool.Highlighter, ToolCursor.Highlighter)]
    [InlineData(EditorTool.Eraser, ToolCursor.Eraser)]     // 지우개면 지우개 모양
    [InlineData(EditorTool.Text, ToolCursor.Text)]
    [InlineData(EditorTool.Rectangle, ToolCursor.Cross)]   // 도형·영역은 정확히 맞추도록 십자
    [InlineData(EditorTool.Ellipse, ToolCursor.Cross)]
    [InlineData(EditorTool.Line, ToolCursor.Cross)]
    [InlineData(EditorTool.Arrow, ToolCursor.Cross)]
    [InlineData(EditorTool.Number, ToolCursor.Cross)]
    [InlineData(EditorTool.Mosaic, ToolCursor.Cross)]
    [InlineData(EditorTool.Blur, ToolCursor.Cross)]
    [InlineData(EditorTool.Crop, ToolCursor.Cross)]
    public void CursorMatchesTool(EditorTool tool, ToolCursor expected) =>
        Assert.Equal(expected, EditorTools.CursorOf(tool));

    [Fact(DisplayName = "shortcuts-match-spec")]
    public void ShortcutsMatchSpec()
    {
        Assert.Equal("VPHRELATNMBCX", new string(EditorTools.Order.Select(EditorTools.ShortcutOf).ToArray()));
    }

    [Fact(DisplayName = "shortcut-case-insensitive")]
    public void ShortcutCaseInsensitive()
    {
        Assert.True(EditorTools.TryFromShortcut('p', out var tool));
        Assert.Equal(EditorTool.Pen, tool);
    }

    [Fact(DisplayName = "unknown-shortcut")]
    public void UnknownShortcut()
    {
        Assert.False(EditorTools.TryFromShortcut('Q', out _));
    }

    [Fact(DisplayName = "default-is-pen")]
    public void DefaultIsPen()
    {
        Assert.Equal(EditorTool.Pen, EditorTools.Default);
    }

    [Fact(DisplayName = "every-tool-has-help")]
    public void EveryToolHasHelp()
    {
        Assert.All(EditorTools.Order, t => Assert.False(string.IsNullOrWhiteSpace(EditorTools.HelpOf(t))));
        foreach (var tool in new[] { EditorTool.Pen, EditorTool.Highlighter })
        {
            Assert.Contains("Shift를 누르면 가로·세로", EditorTools.HelpOf(tool));
            Assert.Contains("Ctrl을 누르면", EditorTools.HelpOf(tool));
        }
    }
}
