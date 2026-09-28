using WireBound.Platform.Windows.Services;

namespace WireBound.Tests.Platform;

public class WindowsTrayIconSizeProviderTests
{
    [Test]
    [Arguments(96u, 16)]
    [Arguments(120u, 20)]
    [Arguments(144u, 24)]
    [Arguments(168u, 28)]
    [Arguments(192u, 32)]
    [Arguments(225u, 38)]
    public void GetPixelSizeForDpi_MatchesAvaloniaSmallIconSize(uint dpi, int expected)
    {
        if (!OperatingSystem.IsWindows()) return;

        WindowsTrayIconSizeProvider.GetPixelSizeForDpi(dpi).Should().Be(expected);
    }
}
