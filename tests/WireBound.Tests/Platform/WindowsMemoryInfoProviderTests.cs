using WireBound.Platform.Windows.Services;

namespace WireBound.Tests.Platform;

public class WindowsMemoryInfoProviderTests
{
    [Test]
    public void NativeMemorySample_IsPhysicalAndInternallyConsistent()
    {
        if (!OperatingSystem.IsWindows()) return;

        var sample = new WindowsMemoryInfoProvider().GetMemoryInfo();
        sample.TotalBytes.Should().BeGreaterThan(0);
        sample.AvailableBytes.Should().BeGreaterThanOrEqualTo(0);
        sample.UsedBytes.Should().Be(sample.TotalBytes - sample.AvailableBytes);
        if (sample.InstalledBytes is { } installed)
            installed.Should().BeGreaterThan(0);
        sample.NonpagedPoolBytes.Should().NotBeNull();
        if (sample.NonpagedPoolBytes is { } pool)
            pool.Should().BeLessThanOrEqualTo(sample.UsedBytes);
    }
}
