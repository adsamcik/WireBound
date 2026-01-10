using WireBound.Models;
using WireBound.Services;

namespace WireBound.Tests.Services;

/// <summary>
/// Tests for IDataPersistenceService interface using mocks
/// These tests verify the interface contract and usage patterns
/// </summary>
public class DataPersistenceServiceInterfaceTests
{
    #region Interface Contract Tests

    [Test]
    public async Task SaveStatsAsync_CanBeMocked()
    {
        // Arrange
        var mockService = Substitute.For<IDataPersistenceService>();
        var stats = new NetworkStats { DownloadSpeedBps = 1_000_000 };

        // Act
        await mockService.SaveStatsAsync(stats);

        // Assert
        await mockService.Received(1).SaveStatsAsync(stats);
    }

    [Test]
    public async Task GetDailyUsageAsync_ReturnsConfiguredData()
    {
        // Arrange
        var mockService = Substitute.For<IDataPersistenceService>();
        var expectedData = new List<DailyUsage>
        {
            new() { Date = new DateOnly(2026, 1, 10), BytesReceived = 1_000_000 }
        };
        mockService.GetDailyUsageAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(expectedData);

        // Act
        var result = await mockService.GetDailyUsageAsync(
            new DateOnly(2026, 1, 1), 
            new DateOnly(2026, 1, 31));

        // Assert
        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result[0].BytesReceived).IsEqualTo(1_000_000);
    }

    [Test]
    public async Task GetHourlyUsageAsync_ReturnsConfiguredData()
    {
        // Arrange
        var mockService = Substitute.For<IDataPersistenceService>();
        var expectedData = new List<HourlyUsage>
        {
            new() { Hour = new DateTime(2026, 1, 10, 12, 0, 0), BytesReceived = 500_000 }
        };
        mockService.GetHourlyUsageAsync(Arg.Any<DateOnly>())
            .Returns(expectedData);

        // Act
        var result = await mockService.GetHourlyUsageAsync(new DateOnly(2026, 1, 10));

        // Assert
        await Assert.That(result.Count).IsEqualTo(1);
    }

    [Test]
    public async Task GetTotalUsageAsync_ReturnsTuple()
    {
        // Arrange
        var mockService = Substitute.For<IDataPersistenceService>();
        mockService.GetTotalUsageAsync().Returns((5_000_000L, 2_500_000L));

        // Act
        var (received, sent) = await mockService.GetTotalUsageAsync();

        // Assert
        await Assert.That(received).IsEqualTo(5_000_000);
        await Assert.That(sent).IsEqualTo(2_500_000);
    }

    [Test]
    public async Task CleanupOldDataAsync_AcceptsRetentionDays()
    {
        // Arrange
        var mockService = Substitute.For<IDataPersistenceService>();

        // Act
        await mockService.CleanupOldDataAsync(30);

        // Assert
        await mockService.Received(1).CleanupOldDataAsync(30);
    }

    [Test]
    public async Task GetSettingsAsync_ReturnsAppSettings()
    {
        // Arrange
        var mockService = Substitute.For<IDataPersistenceService>();
        var expectedSettings = new AppSettings { PollingIntervalMs = 500 };
        mockService.GetSettingsAsync().Returns(expectedSettings);

        // Act
        var result = await mockService.GetSettingsAsync();

        // Assert
        await Assert.That(result.PollingIntervalMs).IsEqualTo(500);
    }

    [Test]
    public async Task SaveSettingsAsync_CanBeMocked()
    {
        // Arrange
        var mockService = Substitute.For<IDataPersistenceService>();
        var settings = new AppSettings { StartWithWindows = true };

        // Act
        await mockService.SaveSettingsAsync(settings);

        // Assert
        await mockService.Received(1).SaveSettingsAsync(
            Arg.Is<AppSettings>(s => s.StartWithWindows == true));
    }

    #endregion

    #region Typical Usage Pattern Tests

    [Test]
    public async Task TypicalDashboardWorkflow_LoadsDataCorrectly()
    {
        // Arrange
        var mockService = Substitute.For<IDataPersistenceService>();
        mockService.GetTotalUsageAsync().Returns((10_000_000_000L, 5_000_000_000L));
        mockService.GetSettingsAsync().Returns(new AppSettings());

        // Act - Simulate dashboard loading
        var settings = await mockService.GetSettingsAsync();
        var (totalReceived, totalSent) = await mockService.GetTotalUsageAsync();

        // Assert
        await Assert.That(settings).IsNotNull();
        await Assert.That(totalReceived).IsEqualTo(10_000_000_000);
        await Assert.That(totalSent).IsEqualTo(5_000_000_000);
    }

    [Test]
    public async Task TypicalHistoryViewWorkflow_LoadsDataCorrectly()
    {
        // Arrange
        var mockService = Substitute.For<IDataPersistenceService>();
        var dailyData = new List<DailyUsage>
        {
            new() { Date = new DateOnly(2026, 1, 8), BytesReceived = 1_000_000_000 },
            new() { Date = new DateOnly(2026, 1, 9), BytesReceived = 1_500_000_000 },
            new() { Date = new DateOnly(2026, 1, 10), BytesReceived = 2_000_000_000 }
        };
        mockService.GetDailyUsageAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(dailyData);

        var hourlyData = new List<HourlyUsage>
        {
            new() { Hour = new DateTime(2026, 1, 10, 8, 0, 0), BytesReceived = 100_000_000 },
            new() { Hour = new DateTime(2026, 1, 10, 9, 0, 0), BytesReceived = 150_000_000 }
        };
        mockService.GetHourlyUsageAsync(Arg.Any<DateOnly>()).Returns(hourlyData);

        // Act - Simulate history view loading
        var weeklyData = await mockService.GetDailyUsageAsync(
            new DateOnly(2026, 1, 4), 
            new DateOnly(2026, 1, 10));
        var todayHourly = await mockService.GetHourlyUsageAsync(new DateOnly(2026, 1, 10));

        // Assert
        await Assert.That(weeklyData.Count).IsEqualTo(3);
        await Assert.That(todayHourly.Count).IsEqualTo(2);
    }

    [Test]
    public async Task SettingsUpdateWorkflow_SavesCorrectly()
    {
        // Arrange
        var mockService = Substitute.For<IDataPersistenceService>();
        var initialSettings = new AppSettings { PollingIntervalMs = 1000 };
        mockService.GetSettingsAsync().Returns(initialSettings);

        // Act - Simulate settings page workflow
        var settings = await mockService.GetSettingsAsync();
        settings.PollingIntervalMs = 500;
        settings.UseIpHelperApi = true;
        await mockService.SaveSettingsAsync(settings);

        // Assert
        await mockService.Received(1).SaveSettingsAsync(
            Arg.Is<AppSettings>(s => s.PollingIntervalMs == 500 && s.UseIpHelperApi == true));
    }

    #endregion
}
