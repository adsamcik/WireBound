using WireBound.Models;

namespace WireBound.Tests.Models;

/// <summary>
/// Tests for DailyUsage model
/// </summary>
public class DailyUsageTests
{
    #region Default Values Tests

    [Test]
    public async Task DailyUsage_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var dailyUsage = new DailyUsage();

        // Assert
        await Assert.That(dailyUsage.Id).IsEqualTo(0);
        await Assert.That(dailyUsage.BytesReceived).IsEqualTo(0);
        await Assert.That(dailyUsage.BytesSent).IsEqualTo(0);
        await Assert.That(dailyUsage.PeakDownloadSpeed).IsEqualTo(0);
        await Assert.That(dailyUsage.PeakUploadSpeed).IsEqualTo(0);
        await Assert.That(dailyUsage.AdapterId).IsEqualTo(string.Empty);
    }

    #endregion

    #region TotalBytes Computed Property Tests

    [Test]
    public async Task TotalBytes_WhenBothZero_ReturnsZero()
    {
        // Arrange
        var dailyUsage = new DailyUsage
        {
            BytesReceived = 0,
            BytesSent = 0
        };

        // Act & Assert
        await Assert.That(dailyUsage.TotalBytes).IsEqualTo(0);
    }

    [Test]
    public async Task TotalBytes_WhenOnlyReceived_ReturnsReceived()
    {
        // Arrange
        var dailyUsage = new DailyUsage
        {
            BytesReceived = 1_000_000,
            BytesSent = 0
        };

        // Act & Assert
        await Assert.That(dailyUsage.TotalBytes).IsEqualTo(1_000_000);
    }

    [Test]
    public async Task TotalBytes_WhenOnlySent_ReturnsSent()
    {
        // Arrange
        var dailyUsage = new DailyUsage
        {
            BytesReceived = 0,
            BytesSent = 500_000
        };

        // Act & Assert
        await Assert.That(dailyUsage.TotalBytes).IsEqualTo(500_000);
    }

    [Test]
    public async Task TotalBytes_WhenBothSet_ReturnsSumOfBoth()
    {
        // Arrange
        var dailyUsage = new DailyUsage
        {
            BytesReceived = 1_000_000_000, // 1 GB received
            BytesSent = 500_000_000        // 500 MB sent
        };

        // Act & Assert
        await Assert.That(dailyUsage.TotalBytes).IsEqualTo(1_500_000_000);
    }

    [Test]
    public async Task TotalBytes_WithLargeValues_HandlesCorrectly()
    {
        // Arrange - Simulate heavy usage day
        var dailyUsage = new DailyUsage
        {
            BytesReceived = 100_000_000_000, // 100 GB received
            BytesSent = 50_000_000_000       // 50 GB sent
        };

        // Act & Assert
        await Assert.That(dailyUsage.TotalBytes).IsEqualTo(150_000_000_000);
    }

    #endregion

    #region Property Assignment Tests

    [Test]
    public async Task DailyUsage_PropertyAssignment_WorksCorrectly()
    {
        // Arrange
        var date = new DateOnly(2026, 1, 10);
        var lastUpdated = new DateTime(2026, 1, 10, 23, 59, 59);
        
        var dailyUsage = new DailyUsage
        {
            Id = 42,
            Date = date,
            AdapterId = "eth0",
            BytesReceived = 5_000_000_000,
            BytesSent = 1_000_000_000,
            PeakDownloadSpeed = 125_000_000, // 125 MB/s
            PeakUploadSpeed = 50_000_000,    // 50 MB/s
            LastUpdated = lastUpdated
        };

        // Assert
        await Assert.That(dailyUsage.Id).IsEqualTo(42);
        await Assert.That(dailyUsage.Date).IsEqualTo(date);
        await Assert.That(dailyUsage.AdapterId).IsEqualTo("eth0");
        await Assert.That(dailyUsage.BytesReceived).IsEqualTo(5_000_000_000);
        await Assert.That(dailyUsage.BytesSent).IsEqualTo(1_000_000_000);
        await Assert.That(dailyUsage.PeakDownloadSpeed).IsEqualTo(125_000_000);
        await Assert.That(dailyUsage.PeakUploadSpeed).IsEqualTo(50_000_000);
        await Assert.That(dailyUsage.LastUpdated).IsEqualTo(lastUpdated);
    }

    #endregion

    #region Date Property Tests

    [Test]
    public async Task DailyUsage_Date_SupportsDateOnlyType()
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.Now);
        var dailyUsage = new DailyUsage { Date = today };

        // Assert
        await Assert.That(dailyUsage.Date).IsEqualTo(today);
    }

    [Test]
    public async Task DailyUsage_Date_CanBeMinValue()
    {
        // Arrange
        var dailyUsage = new DailyUsage { Date = DateOnly.MinValue };

        // Assert
        await Assert.That(dailyUsage.Date).IsEqualTo(DateOnly.MinValue);
    }

    [Test]
    public async Task DailyUsage_Date_CanBeMaxValue()
    {
        // Arrange
        var dailyUsage = new DailyUsage { Date = DateOnly.MaxValue };

        // Assert
        await Assert.That(dailyUsage.Date).IsEqualTo(DateOnly.MaxValue);
    }

    #endregion

    #region Edge Cases

    [Test]
    public async Task DailyUsage_WithEmptyAdapterId_IsValid()
    {
        // Arrange - Empty adapter ID means "all adapters"
        var dailyUsage = new DailyUsage
        {
            AdapterId = string.Empty,
            BytesReceived = 1000,
            BytesSent = 500
        };

        // Assert
        await Assert.That(dailyUsage.AdapterId).IsEmpty();
        await Assert.That(dailyUsage.TotalBytes).IsEqualTo(1500);
    }

    [Test]
    public async Task DailyUsage_PeakSpeeds_CanBeDifferent()
    {
        // Arrange - Asymmetric connection (faster download than upload)
        var dailyUsage = new DailyUsage
        {
            PeakDownloadSpeed = 1_000_000_000, // 1 GB/s download
            PeakUploadSpeed = 100_000_000      // 100 MB/s upload
        };

        // Assert - Peak speeds are independent
        await Assert.That(dailyUsage.PeakDownloadSpeed).IsGreaterThan(dailyUsage.PeakUploadSpeed);
    }

    #endregion
}
