using WireBound.Models;

namespace WireBound.Tests.Models;

/// <summary>
/// Tests for NetworkStats model including formatting methods
/// </summary>
public class NetworkStatsTests
{
    #region Default Values Tests

    [Test]
    public async Task NetworkStats_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var stats = new NetworkStats();

        // Assert
        await Assert.That(stats.DownloadSpeedBps).IsEqualTo(0);
        await Assert.That(stats.UploadSpeedBps).IsEqualTo(0);
        await Assert.That(stats.TotalBytesReceived).IsEqualTo(0);
        await Assert.That(stats.TotalBytesSent).IsEqualTo(0);
        await Assert.That(stats.SessionBytesReceived).IsEqualTo(0);
        await Assert.That(stats.SessionBytesSent).IsEqualTo(0);
        await Assert.That(stats.AdapterId).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task NetworkStats_Timestamp_IsSetToNow()
    {
        // Arrange
        var before = DateTime.Now;

        // Act
        var stats = new NetworkStats();
        var after = DateTime.Now;

        // Assert
        await Assert.That(stats.Timestamp).IsGreaterThanOrEqualTo(before);
        await Assert.That(stats.Timestamp).IsLessThanOrEqualTo(after);
    }

    #endregion

    #region FormatSpeed Tests (via DownloadSpeedFormatted/UploadSpeedFormatted)

    [Test]
    public async Task FormatSpeed_ZeroBytes_ReturnsZeroBps()
    {
        // Arrange
        var stats = new NetworkStats { DownloadSpeedBps = 0 };

        // Act
        var result = stats.DownloadSpeedFormatted;

        // Assert
        await Assert.That(result).IsEqualTo("0 B/s");
    }

    [Test]
    public async Task FormatSpeed_LessThanKB_ReturnsBps()
    {
        // Arrange
        var stats = new NetworkStats { DownloadSpeedBps = 500 };

        // Act
        var result = stats.DownloadSpeedFormatted;

        // Assert
        await Assert.That(result).IsEqualTo("500 B/s");
    }

    [Test]
    public async Task FormatSpeed_ExactlyOneKB_ReturnsKBps()
    {
        // Arrange
        var stats = new NetworkStats { DownloadSpeedBps = 1024 };

        // Act
        var result = stats.DownloadSpeedFormatted;

        // Assert
        await Assert.That(result).IsEqualTo("1.00 KB/s");
    }

    [Test]
    public async Task FormatSpeed_KilobytesRange_ReturnsKBps()
    {
        // Arrange - 500 KB/s
        var stats = new NetworkStats { DownloadSpeedBps = 512_000 };

        // Act
        var result = stats.DownloadSpeedFormatted;

        // Assert
        await Assert.That(result).IsEqualTo("500.00 KB/s");
    }

    [Test]
    public async Task FormatSpeed_ExactlyOneMB_ReturnsMBps()
    {
        // Arrange - 1 MB/s = 1,048,576 bytes
        var stats = new NetworkStats { DownloadSpeedBps = 1_048_576 };

        // Act
        var result = stats.DownloadSpeedFormatted;

        // Assert
        await Assert.That(result).IsEqualTo("1.00 MB/s");
    }

    [Test]
    public async Task FormatSpeed_MegabytesRange_ReturnsMBps()
    {
        // Arrange - 100 MB/s = 104,857,600 bytes
        var stats = new NetworkStats { DownloadSpeedBps = 104_857_600 };

        // Act
        var result = stats.DownloadSpeedFormatted;

        // Assert
        await Assert.That(result).IsEqualTo("100.00 MB/s");
    }

    [Test]
    public async Task FormatSpeed_ExactlyOneGB_ReturnsGBps()
    {
        // Arrange - 1 GB/s = 1,073,741,824 bytes
        var stats = new NetworkStats { DownloadSpeedBps = 1_073_741_824 };

        // Act
        var result = stats.DownloadSpeedFormatted;

        // Assert
        await Assert.That(result).IsEqualTo("1.00 GB/s");
    }

    [Test]
    public async Task FormatSpeed_GigabytesRange_ReturnsGBps()
    {
        // Arrange - 2.5 GB/s
        var stats = new NetworkStats { DownloadSpeedBps = 2_684_354_560 };

        // Act
        var result = stats.DownloadSpeedFormatted;

        // Assert
        await Assert.That(result).IsEqualTo("2.50 GB/s");
    }

    [Test]
    public async Task FormatSpeed_UploadSpeed_FormatsCorrectly()
    {
        // Arrange
        var stats = new NetworkStats { UploadSpeedBps = 5_242_880 }; // 5 MB/s

        // Act
        var result = stats.UploadSpeedFormatted;

        // Assert
        await Assert.That(result).IsEqualTo("5.00 MB/s");
    }

    #endregion

    #region FormatBytes Tests (via SessionReceivedFormatted/SessionSentFormatted)

    [Test]
    public async Task FormatBytes_ZeroBytes_ReturnsZeroB()
    {
        // Arrange
        var stats = new NetworkStats { SessionBytesReceived = 0 };

        // Act
        var result = stats.SessionReceivedFormatted;

        // Assert
        await Assert.That(result).IsEqualTo("0 B");
    }

    [Test]
    public async Task FormatBytes_LessThanKB_ReturnsBytes()
    {
        // Arrange
        var stats = new NetworkStats { SessionBytesReceived = 500 };

        // Act
        var result = stats.SessionReceivedFormatted;

        // Assert
        await Assert.That(result).IsEqualTo("500 B");
    }

    [Test]
    public async Task FormatBytes_ExactlyOneKB_ReturnsKB()
    {
        // Arrange
        var stats = new NetworkStats { SessionBytesReceived = 1024 };

        // Act
        var result = stats.SessionReceivedFormatted;

        // Assert
        await Assert.That(result).IsEqualTo("1.00 KB");
    }

    [Test]
    public async Task FormatBytes_KilobytesRange_ReturnsKB()
    {
        // Arrange - 500 KB
        var stats = new NetworkStats { SessionBytesReceived = 512_000 };

        // Act
        var result = stats.SessionReceivedFormatted;

        // Assert
        await Assert.That(result).IsEqualTo("500.00 KB");
    }

    [Test]
    public async Task FormatBytes_ExactlyOneMB_ReturnsMB()
    {
        // Arrange
        var stats = new NetworkStats { SessionBytesReceived = 1_048_576 };

        // Act
        var result = stats.SessionReceivedFormatted;

        // Assert
        await Assert.That(result).IsEqualTo("1.00 MB");
    }

    [Test]
    public async Task FormatBytes_MegabytesRange_ReturnsMB()
    {
        // Arrange - 256 MB
        var stats = new NetworkStats { SessionBytesReceived = 268_435_456 };

        // Act
        var result = stats.SessionReceivedFormatted;

        // Assert
        await Assert.That(result).IsEqualTo("256.00 MB");
    }

    [Test]
    public async Task FormatBytes_ExactlyOneGB_ReturnsGB()
    {
        // Arrange
        var stats = new NetworkStats { SessionBytesReceived = 1_073_741_824 };

        // Act
        var result = stats.SessionReceivedFormatted;

        // Assert
        await Assert.That(result).IsEqualTo("1.00 GB");
    }

    [Test]
    public async Task FormatBytes_GigabytesRange_ReturnsGB()
    {
        // Arrange - 10 GB
        var stats = new NetworkStats { SessionBytesReceived = 10_737_418_240 };

        // Act
        var result = stats.SessionReceivedFormatted;

        // Assert
        await Assert.That(result).IsEqualTo("10.00 GB");
    }

    [Test]
    public async Task FormatBytes_ExactlyOneTB_ReturnsTB()
    {
        // Arrange - 1 TB = 1,099,511,627,776 bytes
        var stats = new NetworkStats { SessionBytesReceived = 1_099_511_627_776 };

        // Act
        var result = stats.SessionReceivedFormatted;

        // Assert
        await Assert.That(result).IsEqualTo("1.00 TB");
    }

    [Test]
    public async Task FormatBytes_TerabytesRange_ReturnsTB()
    {
        // Arrange - 2.5 TB
        var stats = new NetworkStats { SessionBytesReceived = 2_748_779_069_440 };

        // Act
        var result = stats.SessionReceivedFormatted;

        // Assert
        await Assert.That(result).IsEqualTo("2.50 TB");
    }

    [Test]
    public async Task FormatBytes_SessionSent_FormatsCorrectly()
    {
        // Arrange - 1 GB sent
        var stats = new NetworkStats { SessionBytesSent = 1_073_741_824 };

        // Act
        var result = stats.SessionSentFormatted;

        // Assert
        await Assert.That(result).IsEqualTo("1.00 GB");
    }

    #endregion

    #region Property Assignment Tests

    [Test]
    public async Task NetworkStats_PropertyAssignment_WorksCorrectly()
    {
        // Arrange
        var timestamp = new DateTime(2026, 1, 10, 12, 0, 0);
        var stats = new NetworkStats
        {
            Timestamp = timestamp,
            DownloadSpeedBps = 1_000_000,
            UploadSpeedBps = 500_000,
            TotalBytesReceived = 10_000_000_000,
            TotalBytesSent = 5_000_000_000,
            SessionBytesReceived = 1_000_000_000,
            SessionBytesSent = 500_000_000,
            AdapterId = "test-adapter-id"
        };

        // Assert
        await Assert.That(stats.Timestamp).IsEqualTo(timestamp);
        await Assert.That(stats.DownloadSpeedBps).IsEqualTo(1_000_000);
        await Assert.That(stats.UploadSpeedBps).IsEqualTo(500_000);
        await Assert.That(stats.TotalBytesReceived).IsEqualTo(10_000_000_000);
        await Assert.That(stats.TotalBytesSent).IsEqualTo(5_000_000_000);
        await Assert.That(stats.SessionBytesReceived).IsEqualTo(1_000_000_000);
        await Assert.That(stats.SessionBytesSent).IsEqualTo(500_000_000);
        await Assert.That(stats.AdapterId).IsEqualTo("test-adapter-id");
    }

    #endregion

    #region Edge Cases

    [Test]
    public async Task FormatSpeed_JustBelowKB_ReturnsBps()
    {
        // Arrange - 1023 bytes (just below 1 KB threshold)
        var stats = new NetworkStats { DownloadSpeedBps = 1023 };

        // Act
        var result = stats.DownloadSpeedFormatted;

        // Assert
        await Assert.That(result).IsEqualTo("1023 B/s");
    }

    [Test]
    public async Task FormatSpeed_JustBelowMB_ReturnsKBps()
    {
        // Arrange - Just below 1 MB threshold
        var stats = new NetworkStats { DownloadSpeedBps = 1_048_575 };

        // Act
        var result = stats.DownloadSpeedFormatted;

        // Assert
        await Assert.That(result).Contains("KB/s");
    }

    [Test]
    public async Task FormatSpeed_JustBelowGB_ReturnsMBps()
    {
        // Arrange - Just below 1 GB threshold
        var stats = new NetworkStats { DownloadSpeedBps = 1_073_741_823 };

        // Act
        var result = stats.DownloadSpeedFormatted;

        // Assert
        await Assert.That(result).Contains("MB/s");
    }

    [Test]
    public async Task FormatBytes_LargeValue_HandlesProperly()
    {
        // Arrange - 100 TB
        var stats = new NetworkStats { SessionBytesReceived = 109_951_162_777_600 };

        // Act
        var result = stats.SessionReceivedFormatted;

        // Assert
        await Assert.That(result).IsEqualTo("100.00 TB");
    }

    #endregion
}
