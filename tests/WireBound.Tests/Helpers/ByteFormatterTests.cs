using AwesomeAssertions;
using WireBound.Core.Helpers;

namespace WireBound.Tests.Helpers;

/// <summary>
/// Unit tests for ByteFormatter helper class
/// </summary>
[NotInParallel(Order = 1)]  // Ensure tests run serially due to shared static state
public class ByteFormatterTests
{
    [Before(Test)]
    public Task Setup()
    {
        // Reset to default state before each test
        ByteFormatter.UseSpeedInBits = false;
        return Task.CompletedTask;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // FormatBytes Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    [Arguments(0L, "0 B")]
    [Arguments(1L, "1 B")]
    [Arguments(512L, "512 B")]
    [Arguments(1023L, "1023 B")]
    public void FormatBytes_ByteRange_ReturnsBytes(long bytes, string expected)
    {
        // Act
        var result = ByteFormatter.FormatBytes(bytes);

        // Assert
        result.Should().Be(expected);
    }

    [Test]
    [Arguments(1024L, "1.00 KB")]
    [Arguments(1536L, "1.50 KB")]
    [Arguments(2048L, "2.00 KB")]
    [Arguments(1047552L, "1023.00 KB")]  // Just under 1 MB
    public void FormatBytes_KilobyteRange_ReturnsKilobytes(long bytes, string expected)
    {
        // Act
        var result = ByteFormatter.FormatBytes(bytes);

        // Assert
        result.Should().Be(expected);
    }

    [Test]
    [Arguments(1048576L, "1.00 MB")]
    [Arguments(1572864L, "1.50 MB")]
    [Arguments(104857600L, "100.00 MB")]
    [Arguments(1073217536L, "1023.50 MB")]  // Just under 1 GB
    public void FormatBytes_MegabyteRange_ReturnsMegabytes(long bytes, string expected)
    {
        // Act
        var result = ByteFormatter.FormatBytes(bytes);

        // Assert
        result.Should().Be(expected);
    }

    [Test]
    [Arguments(1073741824L, "1.00 GB")]
    [Arguments(1610612736L, "1.50 GB")]
    [Arguments(10737418240L, "10.00 GB")]
    [Arguments(107374182400L, "100.00 GB")]
    public void FormatBytes_GigabyteRange_ReturnsGigabytes(long bytes, string expected)
    {
        // Act
        var result = ByteFormatter.FormatBytes(bytes);

        // Assert
        result.Should().Be(expected);
    }

    [Test]
    [Arguments(1099511627776L, "1.00 TB")]
    [Arguments(1649267441664L, "1.50 TB")]
    [Arguments(10995116277760L, "10.00 TB")]
    public void FormatBytes_TerabyteRange_ReturnsTerabytes(long bytes, string expected)
    {
        // Act
        var result = ByteFormatter.FormatBytes(bytes);

        // Assert
        result.Should().Be(expected);
    }

    [Test]
    public void FormatBytes_VeryLargeValue_ReturnsTerabytes()
    {
        // Arrange
        long petabyte = 1_125_899_906_842_624; // 1 PB in bytes

        // Act
        var result = ByteFormatter.FormatBytes(petabyte);

        // Assert
        result.Should().Be("1024.00 TB");
    }

    [Test]
    public void FormatBytes_MaxLongValue_DoesNotThrow()
    {
        // Arrange
        long maxValue = long.MaxValue;

        // Act
        var act = () => ByteFormatter.FormatBytes(maxValue);

        // Assert
        act.Should().NotThrow();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // FormatSpeedInBytes Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    [Arguments(0L, "0 B/s")]
    [Arguments(1L, "1 B/s")]
    [Arguments(512L, "512 B/s")]
    [Arguments(1023L, "1023 B/s")]
    public void FormatSpeedInBytes_ByteRange_ReturnsBytesPerSecond(long bytesPerSecond, string expected)
    {
        // Act
        var result = ByteFormatter.FormatSpeedInBytes(bytesPerSecond);

        // Assert
        result.Should().Be(expected);
    }

    [Test]
    [Arguments(1024L, "1.00 KB/s")]
    [Arguments(1536L, "1.50 KB/s")]
    [Arguments(10240L, "10.00 KB/s")]
    [Arguments(102400L, "100.00 KB/s")]
    public void FormatSpeedInBytes_KilobyteRange_ReturnsKilobytesPerSecond(long bytesPerSecond, string expected)
    {
        // Act
        var result = ByteFormatter.FormatSpeedInBytes(bytesPerSecond);

        // Assert
        result.Should().Be(expected);
    }

    [Test]
    [Arguments(1048576L, "1.00 MB/s")]
    [Arguments(1572864L, "1.50 MB/s")]
    [Arguments(104857600L, "100.00 MB/s")]
    public void FormatSpeedInBytes_MegabyteRange_ReturnsMegabytesPerSecond(long bytesPerSecond, string expected)
    {
        // Act
        var result = ByteFormatter.FormatSpeedInBytes(bytesPerSecond);

        // Assert
        result.Should().Be(expected);
    }

    [Test]
    [Arguments(1073741824L, "1.00 GB/s")]
    [Arguments(10737418240L, "10.00 GB/s")]
    public void FormatSpeedInBytes_GigabyteRange_ReturnsGigabytesPerSecond(long bytesPerSecond, string expected)
    {
        // Act
        var result = ByteFormatter.FormatSpeedInBytes(bytesPerSecond);

        // Assert
        result.Should().Be(expected);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // FormatSpeedInBits Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    [Arguments(0L, "0 bps")]
    [Arguments(1L, "8 bps")]
    [Arguments(100L, "800 bps")]
    [Arguments(124L, "992 bps")]
    public void FormatSpeedInBits_BitsRange_ReturnsBitsPerSecond(long bytesPerSecond, string expected)
    {
        // Act
        var result = ByteFormatter.FormatSpeedInBits(bytesPerSecond);

        // Assert
        result.Should().Be(expected);
    }

    [Test]
    [Arguments(125L, "1.00 Kbps")]         // 1000 bps = 1 Kbps
    [Arguments(1000L, "8.00 Kbps")]        // 8000 bps = 8 Kbps
    [Arguments(12500L, "100.00 Kbps")]     // 100000 bps = 100 Kbps
    [Arguments(125000L, "1.00 Mbps")]      // 1000000 bps = 1 Mbps (threshold)
    public void FormatSpeedInBits_KilobitsRange_ReturnsKilobitsPerSecond(long bytesPerSecond, string expected)
    {
        // Act
        var result = ByteFormatter.FormatSpeedInBits(bytesPerSecond);

        // Assert
        result.Should().Be(expected);
    }

    [Test]
    [Arguments(125000L, "1.00 Mbps")]      // 1 Mbps
    [Arguments(1250000L, "10.00 Mbps")]    // 10 Mbps
    [Arguments(12500000L, "100.00 Mbps")]  // 100 Mbps
    [Arguments(125000000L, "1.00 Gbps")]   // 1 Gbps (threshold)
    public void FormatSpeedInBits_MegabitsRange_ReturnsMegabitsPerSecond(long bytesPerSecond, string expected)
    {
        // Act
        var result = ByteFormatter.FormatSpeedInBits(bytesPerSecond);

        // Assert
        result.Should().Be(expected);
    }

    [Test]
    [Arguments(125000000L, "1.00 Gbps")]     // 1 Gbps
    [Arguments(1250000000L, "10.00 Gbps")]   // 10 Gbps
    public void FormatSpeedInBits_GigabitsRange_ReturnsGigabitsPerSecond(long bytesPerSecond, string expected)
    {
        // Act
        var result = ByteFormatter.FormatSpeedInBits(bytesPerSecond);

        // Assert
        result.Should().Be(expected);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // FormatSpeed (UseSpeedInBits switch) Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void FormatSpeed_WhenUseSpeedInBitsFalse_ReturnsBytes()
    {
        // Arrange
        ByteFormatter.UseSpeedInBits = false;
        long bytesPerSecond = 1048576; // 1 MB/s

        // Act
        var result = ByteFormatter.FormatSpeed(bytesPerSecond);

        // Assert
        result.Should().Be("1.00 MB/s");
    }

    [Test]
    public void FormatSpeed_WhenUseSpeedInBitsTrue_ReturnsBits()
    {
        // Arrange
        ByteFormatter.UseSpeedInBits = true;
        long bytesPerSecond = 125000; // 1 Mbps

        // Act
        var result = ByteFormatter.FormatSpeed(bytesPerSecond);

        // Assert
        result.Should().Be("1.00 Mbps");
    }

    [Test]
    public void UseSpeedInBits_DefaultValue_ShouldBeFalse()
    {
        // Note: This test may fail if run after other tests that set the value
        // In practice, we reset it in the setup
        ByteFormatter.UseSpeedInBits.Should().BeFalse();
    }

    [Test]
    public void FormatSpeed_TogglingMode_ReturnsCorrectFormat()
    {
        // Arrange
        long bytesPerSecond = 1048576; // 1 MB/s = 8.39 Mbps

        // Act & Assert - Bytes mode
        ByteFormatter.UseSpeedInBits = false;
        ByteFormatter.FormatSpeed(bytesPerSecond).Should().EndWith("B/s");

        // Act & Assert - Bits mode
        ByteFormatter.UseSpeedInBits = true;
        ByteFormatter.FormatSpeed(bytesPerSecond).Should().EndWith("bps");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Edge Cases Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void FormatBytes_ZeroBytes_ReturnsZeroB()
    {
        // Act
        var result = ByteFormatter.FormatBytes(0);

        // Assert
        result.Should().Be("0 B");
    }

    [Test]
    public void FormatSpeedInBytes_ZeroSpeed_ReturnsZeroBps()
    {
        // Act
        var result = ByteFormatter.FormatSpeedInBytes(0);

        // Assert
        result.Should().Be("0 B/s");
    }

    [Test]
    public void FormatSpeedInBits_ZeroSpeed_ReturnsZeroBps()
    {
        // Act
        var result = ByteFormatter.FormatSpeedInBits(0);

        // Assert
        result.Should().Be("0 bps");
    }

    [Test]
    [Arguments(1024L)]      // 1 KB boundary
    [Arguments(1048576L)]   // 1 MB boundary
    [Arguments(1073741824L)] // 1 GB boundary
    public void FormatBytes_BoundaryValues_ReturnsExactUnits(long bytes)
    {
        // Act
        var result = ByteFormatter.FormatBytes(bytes);

        // Assert
        result.Should().StartWith("1.00");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Format Consistency Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void FormatBytes_AlwaysIncludesSpace()
    {
        // Arrange
        var testValues = new long[] { 0, 100, 1024, 1048576, 1073741824 };

        // Act & Assert
        foreach (var value in testValues)
        {
            var result = ByteFormatter.FormatBytes(value);
            result.Should().Contain(" ", $"for value {value}");
        }
    }

    [Test]
    public void FormatBytes_NonZeroValues_IncludeTwoDecimalPlaces()
    {
        // Arrange
        var testValues = new long[] { 1024, 1048576, 1073741824, 1099511627776 };

        // Act & Assert
        foreach (var value in testValues)
        {
            var result = ByteFormatter.FormatBytes(value);
            // Should match pattern like "1.00 KB"
            result.Should().MatchRegex(@"\d+\.\d{2}\s[KMGT]B");
        }
    }

    [Test]
    public void FormatSpeedInBytes_NonZeroValues_IncludeTwoDecimalPlaces()
    {
        // Arrange
        var testValues = new long[] { 1024, 1048576, 1073741824 };

        // Act & Assert
        foreach (var value in testValues)
        {
            var result = ByteFormatter.FormatSpeedInBytes(value);
            // Should match pattern like "1.00 KB/s"
            result.Should().MatchRegex(@"\d+\.\d{2}\s[KMG]B/s");
        }
    }

    [Test]
    public void FormatSpeedInBits_NonZeroValues_IncludeTwoDecimalPlaces()
    {
        // Arrange
        var testValues = new long[] { 125, 125000, 125000000 }; // 1 Kbps, 1 Mbps, 1 Gbps

        // Act & Assert
        foreach (var value in testValues)
        {
            var result = ByteFormatter.FormatSpeedInBits(value);
            // Should match pattern like "1.00 Kbps"
            result.Should().MatchRegex(@"\d+\.\d{2}\s[KMG]bps");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // FormatSpeed with SpeedUnit overload Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void FormatSpeed_WithBytesPerSecondUnit_ReturnsBytes()
    {
        // Arrange
        long bytesPerSecond = 1048576; // 1 MB/s

        // Act
        var result = ByteFormatter.FormatSpeed(bytesPerSecond, WireBound.Core.Models.SpeedUnit.BytesPerSecond);

        // Assert
        result.Should().Be("1.00 MB/s");
    }

    [Test]
    public void FormatSpeed_WithBitsPerSecondUnit_ReturnsBits()
    {
        // Arrange
        long bytesPerSecond = 125000; // 1 Mbps

        // Act
        var result = ByteFormatter.FormatSpeed(bytesPerSecond, WireBound.Core.Models.SpeedUnit.BitsPerSecond);

        // Assert
        result.Should().Be("1.00 Mbps");
    }

    [Test]
    public void FormatSpeed_WithSpeedUnit_DoesNotUseGlobalSetting()
    {
        // Arrange
        ByteFormatter.UseSpeedInBits = true; // Set global to bits
        long bytesPerSecond = 1048576; // 1 MB/s

        // Act - explicitly request bytes format
        var result = ByteFormatter.FormatSpeed(bytesPerSecond, WireBound.Core.Models.SpeedUnit.BytesPerSecond);

        // Assert - should still return bytes despite global setting
        result.Should().Be("1.00 MB/s");

        // Cleanup
        ByteFormatter.UseSpeedInBits = false;
    }

    [Test]
    [Arguments(0L, "0 B/s")]
    [Arguments(1024L, "1.00 KB/s")]
    [Arguments(1048576L, "1.00 MB/s")]
    [Arguments(1073741824L, "1.00 GB/s")]
    public void FormatSpeed_WithBytesUnit_FormatsCorrectly(long bytesPerSecond, string expected)
    {
        // Act
        var result = ByteFormatter.FormatSpeed(bytesPerSecond, WireBound.Core.Models.SpeedUnit.BytesPerSecond);

        // Assert
        result.Should().Be(expected);
    }

    [Test]
    [Arguments(0L, "0 bps")]
    [Arguments(125L, "1.00 Kbps")]
    [Arguments(125000L, "1.00 Mbps")]
    [Arguments(125000000L, "1.00 Gbps")]
    public void FormatSpeed_WithBitsUnit_FormatsCorrectly(long bytesPerSecond, string expected)
    {
        // Act
        var result = ByteFormatter.FormatSpeed(bytesPerSecond, WireBound.Core.Models.SpeedUnit.BitsPerSecond);

        // Assert
        result.Should().Be(expected);
    }
}
