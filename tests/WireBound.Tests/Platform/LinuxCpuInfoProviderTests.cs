#pragma warning disable CA1416

using WireBound.Platform.Linux.Services;

namespace WireBound.Tests.Platform;

/// <summary>
/// Unit tests for <see cref="LinuxCpuInfoProvider.ParseCpuLine"/> static method.
/// </summary>
public class LinuxCpuInfoProviderTests
{
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // Standard parsing
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    [Test]
    public void ParseCpuLine_StandardLineWithAllTenFields_ReturnsCorrectTotals()
    {
        // Arrange
        // cpu  user=10132153 nice=290696 system=3084719 idle=46828483 iowait=16683 irq=0 softirq=25195 steal=0 guest=0 guest_nice=0
        var line = "cpu  10132153 290696 3084719 46828483 16683 0 25195 0 0 0";

        // Act
        var (usage, idle, total) = LinuxCpuInfoProvider.ParseCpuLine(line);

        // Assert
        usage.Should().Be(0);
        idle.Should().Be(46828483L + 16683L);   // idle + iowait
        total.Should().Be(10132153L + 290696L + 3084719L + 46828483L + 16683L + 0L + 25195L + 0L);
    }

    [Test]
    public void ParseCpuLine_MinimalFiveFields_ReturnsIdleWithoutIowait()
    {
        // Arrange ÔÇö only cpu label + user, nice, system, idle (no iowait+)
        var line = "cpu 1000 200 300 5000";

        // Act
        var (usage, idle, total) = LinuxCpuInfoProvider.ParseCpuLine(line);

        // Assert
        usage.Should().Be(0);
        idle.Should().Be(5000L);                // idle only, no iowait
        total.Should().Be(1000L + 200L + 300L + 5000L);
    }

    [Test]
    public void ParseCpuLine_SixFieldsWithIowait_IncludesIowaitInIdle()
    {
        // Arrange ÔÇö cpu label + user, nice, system, idle, iowait
        var line = "cpu 1000 200 300 5000 100";

        // Act
        var (usage, idle, total) = LinuxCpuInfoProvider.ParseCpuLine(line);

        // Assert
        usage.Should().Be(0);
        idle.Should().Be(5000L + 100L);         // idle + iowait
        total.Should().Be(1000L + 200L + 300L + 5000L + 100L);
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // Edge cases
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    [Test]
    public void ParseCpuLine_AllZeroes_ReturnsZeroTotals()
    {
        // Arrange
        var line = "cpu 0 0 0 0 0 0 0 0 0 0";

        // Act
        var (usage, idle, total) = LinuxCpuInfoProvider.ParseCpuLine(line);

        // Assert
        usage.Should().Be(0);
        idle.Should().Be(0L);
        total.Should().Be(0L);
    }

    [Test]
    public void ParseCpuLine_VeryLargeNumbers_HandlesWithoutOverflow()
    {
        // Arrange ÔÇö values near long max / 10 to avoid overflow on sum
        var line = "cpu 100000000000 200000000000 300000000000 400000000000 50000000000 60000000000 70000000000 80000000000 0 0";

        // Act
        var (usage, idle, total) = LinuxCpuInfoProvider.ParseCpuLine(line);

        // Assert
        usage.Should().Be(0);
        idle.Should().Be(400000000000L + 50000000000L);
        total.Should().Be(100000000000L + 200000000000L + 300000000000L + 400000000000L
                          + 50000000000L + 60000000000L + 70000000000L + 80000000000L);
    }

    [Test]
    public void ParseCpuLine_LessThanFiveParts_ReturnsZeroTuple()
    {
        // Arrange ÔÇö only 3 fields after label
        var line = "cpu 100 200 300";

        // Act
        var (usage, idle, total) = LinuxCpuInfoProvider.ParseCpuLine(line);

        // Assert
        usage.Should().Be(0);
        idle.Should().Be(0L);
        total.Should().Be(0L);
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // Per-core lines
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    [Test]
    public void ParseCpuLine_PerCoreCpu0_ParsesCorrectly()
    {
        // Arrange
        var line = "cpu0 1393280 32966 572056 13343292 6130 0 17875 0 0 0";

        // Act
        var (usage, idle, total) = LinuxCpuInfoProvider.ParseCpuLine(line);

        // Assert
        usage.Should().Be(0);
        idle.Should().Be(13343292L + 6130L);
        total.Should().Be(1393280L + 32966L + 572056L + 13343292L + 6130L + 0L + 17875L + 0L);
    }

    [Test]
    public void ParseCpuLine_PerCoreCpu1_ParsesCorrectly()
    {
        // Arrange
        var line = "cpu1 2500000 10000 400000 12000000 5000 100 8000 200 0 0";

        // Act
        var (usage, idle, total) = LinuxCpuInfoProvider.ParseCpuLine(line);

        // Assert
        usage.Should().Be(0);
        idle.Should().Be(12000000L + 5000L);
        total.Should().Be(2500000L + 10000L + 400000L + 12000000L + 5000L + 100L + 8000L + 200L);
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // Invariant verification
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    [Test]
    public void ParseCpuLine_StandardLine_IdleEqualsIdlePlusIowait()
    {
        // Arrange
        long expectedIdle = 46828483L;
        long expectedIowait = 16683L;
        var line = $"cpu  10132153 290696 3084719 {expectedIdle} {expectedIowait} 0 25195 0 0 0";

        // Act
        var (_, idle, _) = LinuxCpuInfoProvider.ParseCpuLine(line);

        // Assert
        idle.Should().Be(expectedIdle + expectedIowait);
    }

    [Test]
    public void ParseCpuLine_StandardLine_TotalEqualsSumOfAllFields()
    {
        // Arrange
        long user = 1000, nice = 200, system = 300, idleVal = 5000;
        long iowait = 100, irq = 50, softirq = 25, steal = 10;
        var line = $"cpu {user} {nice} {system} {idleVal} {iowait} {irq} {softirq} {steal} 0 0";

        // Act
        var (_, _, total) = LinuxCpuInfoProvider.ParseCpuLine(line);

        // Assert
        total.Should().Be(user + nice + system + idleVal + iowait + irq + softirq + steal);
    }
}
