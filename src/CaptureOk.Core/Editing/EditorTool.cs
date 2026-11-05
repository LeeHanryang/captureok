namespace CaptureOk.Core.Editing;

/// <summary>편집 도구. 값의 순서가 도구 막대의 순서다.</summary>
public enum EditorTool { Select, Pen, Highlighter, Rectangle, Ellipse, Line, Arrow, Text, Number, Mosaic, Blur, Crop, Eraser }

/// <summary>편집 캔버스의 마우스 커서 모양: 펜·형광펜·지우개는 그 도구 모양, 도형·영역 도구는 정확히 맞추도록 십자.</summary>
public enum ToolCursor { Arrow, Pen, Highlighter, Eraser, Text, Cross }

public static class EditorTools
{
    private static readonly (EditorTool Tool, char Key, string Name, string Help)[] Table =
    [
        (EditorTool.Select, 'V', "선택", "선택 — 객체를 눌러 옮기고, Delete로 지웁니다"),
        (EditorTool.Pen, 'P', "펜", "펜 — Shift를 누르면 가로·세로 직선, Ctrl을 누르면 원하는 각도의 직선"),
        (EditorTool.Highlighter, 'H', "형광펜", "형광펜 — Shift를 누르면 가로·세로 직선, Ctrl을 누르면 원하는 각도의 직선"),
        (EditorTool.Rectangle, 'R', "사각형", "사각형 — Shift를 누르면 정사각형"),
        (EditorTool.Ellipse, 'E', "원", "원 — Shift를 누르면 정원"),
        (EditorTool.Line, 'L', "직선", "직선 — Shift를 누르면 45도 단위"),
        (EditorTool.Arrow, 'A', "화살표", "화살표 — Shift를 누르면 45도 단위"),
        (EditorTool.Text, 'T', "텍스트", "텍스트 — 누른 곳에 입력하고, 바깥을 누르거나 Esc로 마칩니다"),
        (EditorTool.Number, 'N', "번호", "번호 — 누를 때마다 1, 2, 3… 순서로 붙습니다"),
        (EditorTool.Mosaic, 'M', "모자이크", "모자이크 — 가릴 영역을 드래그"),
        (EditorTool.Blur, 'B', "블러", "블러 — 흐리게 할 영역을 드래그"),
        (EditorTool.Crop, 'C', "자르기", "자르기 — 테두리를 끌어 줄이거나 새로 드래그하고 Enter, 취소는 Esc"),
        (EditorTool.Eraser, 'X', "지우개", "지우개 — 지울 객체를 누르거나 문질러 지웁니다"),
    ];

    public static IReadOnlyList<EditorTool> Order { get; } = Table.Select(t => t.Tool).ToArray();

    public static EditorTool Default => EditorTool.Pen;

    public static char ShortcutOf(EditorTool tool) => Find(tool).Key;

    public static string NameOf(EditorTool tool) => Find(tool).Name;

    public static string HelpOf(EditorTool tool) => Find(tool).Help;

    public static ToolCursor CursorOf(EditorTool tool) => tool switch
    {
        EditorTool.Select => ToolCursor.Arrow,
        EditorTool.Pen => ToolCursor.Pen,
        EditorTool.Highlighter => ToolCursor.Highlighter,
        EditorTool.Eraser => ToolCursor.Eraser,
        EditorTool.Text => ToolCursor.Text,
        _ => ToolCursor.Cross,
    };

    public static bool TryFromShortcut(char key, out EditorTool tool)
    {
        var upper = char.ToUpperInvariant(key);
        foreach (var entry in Table)
        {
            if (entry.Key == upper)
            {
                tool = entry.Tool;
                return true;
            }
        }
        tool = default;
        return false;
    }

    private static (EditorTool Tool, char Key, string Name, string Help) Find(EditorTool tool) => Table.First(t => t.Tool == tool);
}
