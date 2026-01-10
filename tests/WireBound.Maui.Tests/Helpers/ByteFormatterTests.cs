using FluentAssertions;
using TUnit.Core;
using WireBound.Maui.Helpers;

namespace WireBound.Maui.Tests.Helpers;

public class ByteFormatterTests
{
    [Test]
    public async Task FormatSpeed_ShouldFormatBytesPerSecond()
    {
        // Arrange & Act & Assert
        ByteFormatter.FormatSpeed(0).Should().Be("0 B/s");
        ByteFormatter.FormatSpeed(512).Should().Be("512 B/s");
        ByteFormatter.FormatSpeed(1024).Should().Be("1.00 KB/s");
        ByteFormatter.FormatSpeed(1536).Should().Be("1.50 KB/s");
        ByteFormatter.FormatSpeed(1_048_576).Should().Be("1.00 MB/s");
        ByteFormatter.FormatSpeed(1_610_612).Should().Be("1.54 MB/s");
        ByteFormatter.FormatSpeed(1_073_741_824).Should().Be("1.00 GB/s");

        await Task.CompletedTask;
    }

    [Test]
    public async Task FormatBytes_ShouldFormatBytes()
    {
        // Arrange & Act & Assert
        ByteFormatter.FormatBytes(0).Should().Be("0 B");
        ByteFormatter.FormatBytes(512).Should().Be("512 B");
        ByteFormatter.FormatBytes(1024).Should().Be("1.00 KB");
        ByteFormatter.FormatBytes(1_048_576).Should().Be("1.00 MB");
        ByteFormatter.FormatBytes(1_073_741_824).Should().Be("1.00 GB");
        ByteFormatter.FormatBytes(1_099_511_627_776).Should().Be("1.00 TB");

        await Task.CompletedTask;
    }

    [Test]
    public async Task FormatSpeed_WithNegativeValues_ShouldHandleGracefully()
    {
        // Negative values should still format (edge case)
        var result = ByteFormatter.FormatSpeed(-100);
        result.Should().Contain("-100");

        await Task.CompletedTask;
    }

    [Test]
    public async Task FormatBytes_WithLargeValues_ShouldFormatCorrectly()
    {
        // Test very large values (10 TB)
        var tenTB = 10L * 1_099_511_627_776;
        var result = ByteFormatter.FormatBytes(tenTB);
        result.Should().Be("10.00 TB");

        await Task.CompletedTask;
    }
}
