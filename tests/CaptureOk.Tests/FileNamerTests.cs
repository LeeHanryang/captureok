using CaptureOk.Core.Files;

namespace CaptureOk.Tests;

public class FileNamerTests
{
    private const string Folder = @"D:\shots";
    private static readonly DateTime Now = new(2026, 10, 27, 9, 5, 3);

    private static string Name(string template, params string[] existing) =>
        Path.GetFileName(FileNamer.Next(Folder, template, Now, ".png", p => existing.Contains(Path.GetFileName(p))));

    [Fact(DisplayName = "default-template")]
    public void DefaultTemplate()
    {
        Assert.Equal(@"D:\shots\20261027_090503.png", FileNamer.Next(Folder, "%Y%m%d_%H%M%S", Now, ".png", _ => false));
    }

    [Fact(DisplayName = "adds-suffix-on-conflict")]
    public void AddsSuffixOnConflict()
    {
        Assert.Equal("20261027_090503_2.png", Name("%Y%m%d_%H%M%S", "20261027_090503.png", "20261027_090503_1.png"));
    }

    [Fact(DisplayName = "literal-text-kept")]
    public void LiteralTextKept()
    {
        Assert.Equal("capture_0905.png", Name("capture_%H%M"));
    }

    [Fact(DisplayName = "invalid-chars-replaced")]
    public void InvalidCharsReplaced()
    {
        Assert.Equal("a_b_2026.png", Name("a:b?%Y"));
    }

    [Fact(DisplayName = "unknown-token-becomes-literal")]
    public void UnknownTokenBecomesLiteral()
    {
        Assert.Equal("_Q.png", Name("%Q"));
    }
}
