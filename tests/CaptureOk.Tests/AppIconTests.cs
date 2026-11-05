using System.Drawing;

namespace CaptureOk.Tests;

public class AppIconTests
{
    private static string IconPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CaptureOk.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "CaptureOk", "Assets", "CaptureOk.ico");
    }

    [Fact(DisplayName = "icon-file-has-every-size")]
    public void IconFileHasEverySize()
    {
        var bytes = File.ReadAllBytes(IconPath());
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 2)); // 아이콘
        var count = BitConverter.ToUInt16(bytes, 4);
        var sizes = Enumerable.Range(0, count).Select(i => bytes[6 + i * 16] == 0 ? 256 : bytes[6 + i * 16]).ToArray();
        Assert.Equal([16, 20, 24, 32, 40, 48, 64, 256], sizes);
    }

    [Theory(DisplayName = "tray-sizes-load-exactly")]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(32)]
    public void TraySizesLoadExactly(int size)
    {
        using var stream = File.OpenRead(IconPath());
        using var icon = CaptureOk.Tray.TrayHost.LoadIcon(stream, size);
        Assert.Equal(size, icon.Width);
        using var bitmap = icon.ToBitmap();
        Assert.Equal(255, bitmap.GetPixel(size / 2, 2).A); // 바탕(진회색 둥근 사각형)이 불투명하게 들어 있음
    }
}
