using WireBound.Models;

namespace WireBound.Tests.Models;

/// <summary>
/// Tests for HourlyUsage model
/// </summary>
public class HourlyUsageTests
{
    #region Default Values Tests

    [Test]
    public async Task HourlyUsage_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var hourlyUsage = new HourlyUsage();

        // Assert
        await Assert.That(hourlyUsage.Id).IsEqualTo(0);
        await Assert.That(hourlyUsage.BytesReceived).IsEqualTo(0);
        await Assert.That(hourlyUsage.BytesSent).IsEqualTo(0);
        await Assert.That(hourlyUsage.PeakDownloadSpeed).IsEqualTo(0);
        await Assert.That(hourlyUsage.PeakUploadSpeed).IsEqualTo(0);
        await Assert.That(hourlyUsage.AdapterId).IsEqualTo(string.Empty);
    }

    #endregion

    #region Property Assignment Tests

    [Test]
    public async Task HourlyUsage_PropertyAssignment_WorksCorrectly()
    {
        // Arrange
        var hour = new DateTime(2026, 1, 10, 14, 0, 0); // 2 PM
        var lastUpdated = new DateTime(2026, 1, 10, 14, 59, 59);
        
        var hourlyUsage = new HourlyUsage
        {
            Id = 123,
            Hour = hour,
            AdapterId = "wifi-adapter",
            BytesReceived = 500_000_000,   // 500 MB
            BytesSent = 100_000_000,       // 100 MB
            PeakDownloadSpeed = 50_000_000, // 50 MB/s
            PeakUploadSpeed = 10_000_000,   // 10 MB/s
            LastUpdated = lastUpdated
        };

        // Assert
        await Assert.That(hourlyUsage.Id).IsEqualTo(123);
        await Assert.That(hourlyUsage.Hour).IsEqualTo(hour);
        await Assert.That(hourlyUsage.AdapterId).IsEqualTo("wifi-adapter");
        await Assert.That(hourlyUsage.BytesReceived).IsEqualTo(500_000_000);
        await Assert.That(hourlyUsage.BytesSent).IsEqualTo(100_000_000);
        await Assert.That(hourlyUsage.PeakDownloadSpeed).IsEqualTo(50_000_000);
        await Assert.That(hourlyUsage.PeakUploadSpeed).IsEqualTo(10_000_000);
        await Assert.That(hourlyUsage.LastUpdated).IsEqualTo(lastUpdated);
    }

    #endregion

    #region Hour Property Tests

    [Test]
    public async Task HourlyUsage_Hour_ShouldBeTruncatedToHour()
    {
        // Arrange - Hour should represent start of hour (minutes/seconds = 0)
        var truncatedHour = new DateTime(2026, 1, 10, 15, 0, 0);
        var hourlyUsage = new HourlyUsage { Hour = truncatedHour };

        // Assert
        await Assert.That(hourlyUsage.Hour.Minute).IsEqualTo(0);
        await Assert.That(hourlyUsage.Hour.Second).IsEqualTo(0);
    }

    [Test]
    public async Task HourlyUsage_Hour_MidnightIsValid()
    {
        // Arrange
        var midnight = new DateTime(2026, 1, 10, 0, 0, 0);
        var hourlyUsage = new HourlyUsage { Hour = midnight };

        // Assert
        await Assert.That(hourlyUsage.Hour).IsEqualTo(midnight);
        await Assert.That(hourlyUsage.Hour.Hour).IsEqualTo(0);
    }

    [Test]
    public async Task HourlyUsage_Hour_LastHourOfDayIsValid()
    {
        // Arrange
        var lastHour = new DateTime(2026, 1, 10, 23, 0, 0);
        var hourlyUsage = new HourlyUsage { Hour = lastHour };

        // Assert
        await Assert.That(hourlyUsage.Hour).IsEqualTo(lastHour);
        await Assert.That(hourlyUsage.Hour.Hour).IsEqualTo(23);
    }

    #endregion

    #region Usage Data Tests

    [Test]
    public async Task HourlyUsage_CanAccumulateBytes_DuringHour()
    {
        // Arrange - Simulate multiple updates during an hour
        var hourlyUsage = new HourlyUsage
        {
            BytesReceived = 0,
            BytesSent = 0
        };

        // Act - Simulate accumulating data
        hourlyUsage.BytesReceived += 100_000_000;  // First update: 100 MB
        hourlyUsage.BytesReceived += 150_000_000;  // Second update: 150 MB
        hourlyUsage.BytesSent += 25_000_000;       // First upload: 25 MB
        hourlyUsage.BytesSent += 50_000_000;       // Second upload: 50 MB

        // Assert
        await Assert.That(hourlyUsage.BytesReceived).IsEqualTo(250_000_000);
        await Assert.That(hourlyUsage.BytesSent).IsEqualTo(75_000_000);
    }

    [Test]
    public async Task HourlyUsage_PeakSpeed_ShouldTrackMaximum()
    {
        // Arrange
        var hourlyUsage = new HourlyUsage
        {
            PeakDownloadSpeed = 0,
            PeakUploadSpeed = 0
        };

        // Act - Simulate tracking peak speeds
        var speeds = new long[] { 10_000_000, 50_000_000, 30_000_000, 75_000_000, 25_000_000 };
        foreach (var speed in speeds)
        {
            hourlyUsage.PeakDownloadSpeed = Math.Max(hourlyUsage.PeakDownloadSpeed, speed);
        }

        // Assert - Peak should be the maximum observed
        await Assert.That(hourlyUsage.PeakDownloadSpeed).IsEqualTo(75_000_000);
    }

    #endregion

    #region Edge Cases

    [Test]
    public async Task HourlyUsage_WithEmptyAdapterId_IsValid()
    {
        // Arrange - Empty adapter ID means aggregated data
        var hourlyUsage = new HourlyUsage
        {
            AdapterId = string.Empty,
            BytesReceived = 1000,
            BytesSent = 500
        };

        // Assert
        await Assert.That(hourlyUsage.AdapterId).IsEmpty();
    }

    [Test]
    public async Task HourlyUsage_ZeroUsage_IsValid()
    {
        // Arrange - No traffic during this hour
        var hour = new DateTime(2026, 1, 10, 3, 0, 0); // 3 AM - likely low traffic
        var hourlyUsage = new HourlyUsage
        {
            Hour = hour,
            BytesReceived = 0,
            BytesSent = 0,
            PeakDownloadSpeed = 0,
            PeakUploadSpeed = 0
        };

        // Assert
        await Assert.That(hourlyUsage.BytesReceived).IsEqualTo(0);
        await Assert.That(hourlyUsage.BytesSent).IsEqualTo(0);
        await Assert.That(hourlyUsage.PeakDownloadSpeed).IsEqualTo(0);
        await Assert.That(hourlyUsage.PeakUploadSpeed).IsEqualTo(0);
    }

    [Test]
    public async Task HourlyUsage_LargeTraffic_HandlesCorrectly()
    {
        // Arrange - High traffic hour (e.g., large download)
        var hourlyUsage = new HourlyUsage
        {
            BytesReceived = 50_000_000_000,  // 50 GB in one hour
            BytesSent = 5_000_000_000,       // 5 GB in one hour
            PeakDownloadSpeed = 500_000_000, // 500 MB/s peak
            PeakUploadSpeed = 100_000_000    // 100 MB/s peak
        };

        // Assert
        await Assert.That(hourlyUsage.BytesReceived).IsEqualTo(50_000_000_000);
        await Assert.That(hourlyUsage.BytesSent).IsEqualTo(5_000_000_000);
    }

    #endregion

    #region LastUpdated Tests

    [Test]
    public async Task HourlyUsage_LastUpdated_CanBeWithinHour()
    {
        // Arrange
        var hour = new DateTime(2026, 1, 10, 14, 0, 0);
        var lastUpdated = new DateTime(2026, 1, 10, 14, 45, 30);
        
        var hourlyUsage = new HourlyUsage
        {
            Hour = hour,
            LastUpdated = lastUpdated
        };

        // Assert
        await Assert.That(hourlyUsage.LastUpdated).IsGreaterThan(hourlyUsage.Hour);
        await Assert.That(hourlyUsage.LastUpdated.Hour).IsEqualTo(hourlyUsage.Hour.Hour);
    }

    #endregion
}
