namespace CaptureOk.Core.Editing;

/// <summary>편집 도구 옵션 목록과 고정 값.</summary>
public static class DrawingOptions
{
    public const byte HighlighterAlpha = 0x66;
    public const double HighlighterWidthFactor = 4;
    public const double DefaultFontSize = 20;
    public const double DefaultNumberDiameter = 28;

    public static IReadOnlyList<(string Name, uint Argb)> Palette { get; } =
    [
        ("빨강", 0xFFFF0000), ("주황", 0xFFFF8C00), ("노랑", 0xFFFFD400), ("초록", 0xFF22B14C),
        ("파랑", 0xFF1E6FFF), ("보라", 0xFF8E44AD), ("검정", 0xFF000000), ("흰색", 0xFFFFFFFF),
    ];

    public static IReadOnlyList<int> Widths { get; } = [1, 2, 3, 5, 8];
    public static IReadOnlyList<double> FontSizes { get; } = [12, 16, 20, 28, 36];
    public static IReadOnlyList<double> NumberDiameters { get; } = [20, 28, 36];

    public const int DefaultMosaicBlock = 12;
    public static IReadOnlyList<int> MosaicBlockSizes { get; } = [8, 12, 16, 24];

    public const int DefaultBlurRadius = 8;
    public static IReadOnlyList<(string Name, int Radius)> BlurStrengths { get; } = [("약", 4), ("중", 8), ("강", 16)];
}
