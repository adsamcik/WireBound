using FluentAssertions;
using WireBound.Core.Helpers;

namespace WireBound.Tests.Helpers;

/// <summary>
/// Unit tests for ByteFormatter helper class
/// </summary>
public class ByteFormatterTests
{
    public ByteFormatterTests()
    {
        // Reset to default state before each test
        ByteFormatter.UseSpeedInBits = false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // FormatBytes Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1, "1 B")]
    [InlineData(512, "512 B")]
    [InlineData(1023, "1023 B")]
    public void FormatBytes_ByteRange_ReturnsBytes(long bytes, string expected)
    {
        // Act
        var result = ByteFormatter.FormatBytes(bytes);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(1024, "1.00 KB")]
    [InlineData(1536, "1.50 KB")]
    [InlineData(2048, "2.00 KB")]
    [InlineData(1047552, "1023.00 KB")]  // Just under 1 MB
    public void FormatBytes_KilobyteRange_ReturnsKilobytes(long bytes, string expected)
    {
        // Act
        var result = ByteFormatter.FormatBytes(bytes);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(1048576, "1.00 MB")]
    [InlineData(1572864, "1.50 MB")]
    [InlineData(104857600, "100.00 MB")]
    [InlineData(1073217536, "1023.50 MB")]  // Just under 1 GB
    public void FormatBytes_MegabyteRange_ReturnsMegabytes(long bytes, string expected)
    {
        // Act
        var result = ByteFormatter.FormatBytes(bytes);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(1073741824, "1.00 GB")]
    [InlineData(1610612736, "1.50 GB")]
    [InlineData(10737418240, "10.00 GB")]
    [InlineData(107374182400, "100.00 GB")]
    public void FormatBytes_GigabyteRange_ReturnsGigabytes(long bytes, string expected)
    {
        // Act
        var result = ByteFormatter.FormatBytes(bytes);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(1099511627776, "1.00 TB")]
    [InlineData(1649267441664, "1.50 TB")]
    [InlineData(10995116277760, "10.00 TB")]
    public void FormatBytes_TerabyteRange_ReturnsTerabytes(long bytes, string expected)
    {
        // Act
        var result = ByteFormatter.FormatBytes(bytes);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void FormatBytes_VeryLargeValue_ReturnsTerabytes()
    {
        // Arrange
        long petabyte = 1_125_899_906_842_624; // 1 PB in bytes

        // Act
        var result = ByteFormatter.FormatBytes(petabyte);

        // Assert
        result.Should().Be("1024.00 TB");
    }

    [Fact]
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

    [Theory]
    [InlineData(0, "0 B/s")]
    [InlineData(1, "1 B/s")]
    [InlineData(512, "512 B/s")]
    [InlineData(1023, "1023 B/s")]
    public void FormatSpeedInBytes_ByteRange_ReturnsBytesPerSecond(long bytesPerSecond, string expected)
    {
        // Act
        var result = ByteFormatter.FormatSpeedInBytes(bytesPerSecond);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(1024, "1.00 KB/s")]
    [InlineData(1536, "1.50 KB/s")]
    [InlineData(10240, "10.00 KB/s")]
    [InlineData(102400, "100.00 KB/s")]
    public void FormatSpeedInBytes_KilobyteRange_ReturnsKilobytesPerSecond(long bytesPerSecond, string expected)
    {
        // Act
        var result = ByteFormatter.FormatSpeedInBytes(bytesPerSecond);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(1048576, "1.00 MB/s")]
    [InlineData(1572864, "1.50 MB/s")]
    [InlineData(104857600, "100.00 MB/s")]
    public void FormatSpeedInBytes_MegabyteRange_ReturnsMegabytesPerSecond(long bytesPerSecond, string expected)
    {
        // Act
        var result = ByteFormatter.FormatSpeedInBytes(bytesPerSecond);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(1073741824, "1.00 GB/s")]
    [InlineData(10737418240, "10.00 GB/s")]
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

    [Theory]
    [InlineData(0, "0 bps")]
    [InlineData(1, "8 bps")]
    [InlineData(100, "800 bps")]
    [InlineData(124, "992 bps")]
    public void FormatSpeedInBits_BitsRange_ReturnsBitsPerSecond(long bytesPerSecond, string expected)
    {
        // Act
        var result = ByteFormatter.FormatSpeedInBits(bytesPerSecond);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(125, "1.00 Kbps")]         // 1000 bps = 1 Kbps
    [InlineData(1000, "8.00 Kbps")]        // 8000 bps = 8 Kbps
    [InlineData(12500, "100.00 Kbps")]     // 100000 bps = 100 Kbps
    [InlineData(125000, "1.00 Mbps")]      // 1000000 bps = 1 Mbps (threshold)
    public void FormatSpeedInBits_KilobitsRange_ReturnsKilobitsPerSecond(long bytesPerSecond, string expected)
    {
        // Act
        var result = ByteFormatter.FormatSpeedInBits(bytesPerSecond);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(125000, "1.00 Mbps")]      // 1 Mbps
    [InlineData(1250000, "10.00 Mbps")]    // 10 Mbps
    [InlineData(12500000, "100.00 Mbps")]  // 100 Mbps
    [InlineData(125000000, "1.00 Gbps")]   // 1 Gbps (threshold)
    public void FormatSpeedInBits_MegabitsRange_ReturnsMegabitsPerSecond(long bytesPerSecond, string expected)
    {
        // Act
        var result = ByteFormatter.FormatSpeedInBits(bytesPerSecond);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(125000000, "1.00 Gbps")]     // 1 Gbps
    [InlineData(1250000000, "10.00 Gbps")]   // 10 Gbps
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

    [Fact]
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

    [Fact]
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

    [Fact]
    public void UseSpeedInBits_DefaultValue_ShouldBeFalse()
    {
        // Note: This test may fail if run after other tests that set the value
        // In practice, we reset it in the constructor
        ByteFormatter.UseSpeedInBits.Should().BeFalse();
    }

    [Fact]
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

    [Fact]
    public void FormatBytes_ZeroBytes_ReturnsZeroB()
    {
        // Act
        var result = ByteFormatter.FormatBytes(0);

        // Assert
        result.Should().Be("0 B");
    }

    [Fact]
    public void FormatSpeedInBytes_ZeroSpeed_ReturnsZeroBps()
    {
        // Act
        var result = ByteFormatter.FormatSpeedInBytes(0);

        // Assert
        result.Should().Be("0 B/s");
    }

    [Fact]
    public void FormatSpeedInBits_ZeroSpeed_ReturnsZeroBps()
    {
        // Act
        var result = ByteFormatter.FormatSpeedInBits(0);

        // Assert
        result.Should().Be("0 bps");
    }

    [Theory]
    [InlineData(1024)]      // 1 KB boundary
    [InlineData(1048576)]   // 1 MB boundary
    [InlineData(1073741824)] // 1 GB boundary
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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
}
