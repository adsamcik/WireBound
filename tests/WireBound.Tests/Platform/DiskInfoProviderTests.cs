#pragma warning disable CA1416

using WireBound.Platform.Linux.Services;
using WireBound.Platform.Stub.Services;

namespace WireBound.Tests.Platform;

/// <summary>
/// Unit tests for the disk info providers, focusing on the physical-disk
/// filtering logic that avoids double counting partitions and virtual devices.
/// </summary>
public class DiskInfoProviderTests
{
    [Test]
    [Arguments("sda")]
    [Arguments("sdb")]
    [Arguments("vda")]
    [Arguments("hdc")]
    [Arguments("nvme0n1")]
    [Arguments("nvme1n1")]
    [Arguments("mmcblk0")]
    public void IsPhysicalDisk_WholeDisks_ReturnsTrue(string name)
    {
        LinuxDiskInfoProvider.IsPhysicalDisk(name).Should().BeTrue();
    }

    [Test]
    [Arguments("sda1")]
    [Arguments("sdb2")]
    [Arguments("vda1")]
    [Arguments("nvme0n1p1")]
    [Arguments("nvme0n1p2")]
    [Arguments("mmcblk0p1")]
    public void IsPhysicalDisk_Partitions_ReturnsFalse(string name)
    {
        LinuxDiskInfoProvider.IsPhysicalDisk(name).Should().BeFalse();
    }

    [Test]
    [Arguments("loop0")]
    [Arguments("ram0")]
    [Arguments("zram0")]
    [Arguments("dm-0")]
    [Arguments("md0")]
    [Arguments("sr0")]
    [Arguments("")]
    public void IsPhysicalDisk_VirtualDevices_ReturnsFalse(string name)
    {
        LinuxDiskInfoProvider.IsPhysicalDisk(name).Should().BeFalse();
    }

    [Test]
    public void StubDiskInfoProvider_GetDiskInfo_ReturnsNonNegativeValues()
    {
        // Arrange
        var provider = new StubDiskInfoProvider();

        // Act
        var info = provider.GetDiskInfo();

        // Assert
        info.ReadBytesPerSecond.Should().BeGreaterThanOrEqualTo(0);
        info.WriteBytesPerSecond.Should().BeGreaterThanOrEqualTo(0);
        info.ActivityPercent.Should().BeInRange(0, 100);
        provider.SupportsActivityPercent.Should().BeTrue();
    }
}
