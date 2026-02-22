using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WireBound.Avalonia.Services;
using WireBound.Core.Data;
using WireBound.Core.Models;
using WireBound.Platform.Abstract.Models;
using WireBound.Tests.Fixtures;

namespace WireBound.Tests.Services;

/// <summary>
/// Unit tests for DataPersistenceService
/// </summary>
public class DataPersistenceServiceTests : DatabaseTestBase
{
    private readonly DataPersistenceService _service;

    public DataPersistenceServiceTests()
    {
        _service = new DataPersistenceService(ServiceProvider);
    }

    #region Helper Methods

    private static NetworkStats CreateNetworkStats(
        long sessionReceived = 1000,
        long sessionSent = 500,
        long downloadSpeedBps = 100_000,
        long uploadSpeedBps = 50_000,
        string adapterId = "test-adapter")
    {
        return new NetworkStats
        {
            Timestamp = DateTime.Now,
            AdapterId = adapterId,
            SessionBytesReceived = sessionReceived,
            SessionBytesSent = sessionSent,
            DownloadSpeedBps = downloadSpeedBps,
            UploadSpeedBps = uploadSpeedBps
        };
    }

    #endregion

    // ═══════════════════════════════════════════════════════════════════════
    // SaveStatsAsync Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test, Timeout(30000)]
    public async Task SaveStatsAsync_FirstSave_CreatesHourlyAndDailyRecords(CancellationToken cancellationToken)
    {
        // Arrange
        var stats = CreateNetworkStats(sessionReceived: 1000, sessionSent: 500);

        // Act
        await _service.SaveStatsAsync(stats);

        // Assert
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var hourlyCount = await db.HourlyUsages.CountAsync();
        var dailyCount = await db.DailyUsages.CountAsync();

        hourlyCount.Should().Be(1);
        dailyCount.Should().Be(1);
    }

    [Test, Timeout(30000)]
    public async Task SaveStatsAsync_MultipleSavesToSameHour_UpdatesExistingRecords(CancellationToken cancellationToken)
    {
        // Arrange
        var stats1 = CreateNetworkStats(sessionReceived: 1000, sessionSent: 500);
        var stats2 = CreateNetworkStats(sessionReceived: 2000, sessionSent: 1000);

        // Act
        await _service.SaveStatsAsync(stats1);
        await _service.SaveStatsAsync(stats2);

        // Assert
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var hourlyCount = await db.HourlyUsages.CountAsync();
        hourlyCount.Should().Be(1);

        var hourly = await db.HourlyUsages.FirstAsync();
        // Should contain delta: (2000-1000) + 1000 = 2000 total
        hourly.BytesReceived.Should().Be(2000);
        hourly.BytesSent.Should().Be(1000);
    }

    [Test, Timeout(30000)]
    public async Task SaveStatsAsync_ZeroDelta_DoesNotInflateRecords(CancellationToken cancellationToken)
    {
        // Arrange - save the same session counters twice (zero delta on second call)
        var stats = CreateNetworkStats(sessionReceived: 1000, sessionSent: 500);

        // Act
        await _service.SaveStatsAsync(stats);
        await _service.SaveStatsAsync(stats); // Same values = zero delta

        // Assert - bytes should be 1000/500, NOT 2000/1000
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var hourly = await db.HourlyUsages.FirstAsync();
        hourly.BytesReceived.Should().Be(1000);
        hourly.BytesSent.Should().Be(500);
    }

    [Test, Timeout(30000)]
    public async Task SaveStatsAsync_TracksPeakSpeeds(CancellationToken cancellationToken)
    {
        // Arrange
        var stats1 = CreateNetworkStats(downloadSpeedBps: 100_000, uploadSpeedBps: 50_000);
        var stats2 = CreateNetworkStats(downloadSpeedBps: 200_000, uploadSpeedBps: 30_000);

        // Make sure we're tracking session bytes correctly
        stats1 = CreateNetworkStats(sessionReceived: 1000, sessionSent: 500, downloadSpeedBps: 100_000, uploadSpeedBps: 50_000);
        stats2 = CreateNetworkStats(sessionReceived: 2000, sessionSent: 1000, downloadSpeedBps: 200_000, uploadSpeedBps: 30_000);

        // Act
        await _service.SaveStatsAsync(stats1);
        await _service.SaveStatsAsync(stats2);

        // Assert
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var hourly = await db.HourlyUsages.FirstAsync();
        hourly.PeakDownloadSpeed.Should().Be(200_000); // Max of 100k and 200k
        hourly.PeakUploadSpeed.Should().Be(50_000); // Max of 50k and 30k
    }

    [Test, Timeout(30000)]
    public async Task SaveStatsAsync_DifferentAdapters_CreatesSeparateRecords(CancellationToken cancellationToken)
    {
        // Arrange
        var stats1 = CreateNetworkStats(adapterId: "adapter-1", sessionReceived: 1000);
        var stats2 = CreateNetworkStats(adapterId: "adapter-2", sessionReceived: 2000);

        // Act
        await _service.SaveStatsAsync(stats1);
        await _service.SaveStatsAsync(stats2);

        // Assert
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var hourlyCount = await db.HourlyUsages.CountAsync();
        var dailyCount = await db.DailyUsages.CountAsync();

        hourlyCount.Should().Be(2);
        dailyCount.Should().Be(2);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GetDailyUsageAsync Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test, Timeout(30000)]
    public async Task GetDailyUsageAsync_NoData_ReturnsEmptyList(CancellationToken cancellationToken)
    {
        // Arrange
        var startDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-7));
        var endDate = DateOnly.FromDateTime(DateTime.Now);

        // Act
        var result = await _service.GetDailyUsageAsync(startDate, endDate);

        // Assert
        result.Should().BeEmpty();
    }

    [Test, Timeout(30000)]
    public async Task GetDailyUsageAsync_WithData_ReturnsOrderedByDate(CancellationToken cancellationToken)
    {
        // Arrange - Seed data
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();
            db.DailyUsages.AddRange(
                new DailyUsage { Date = DateOnly.FromDateTime(DateTime.Now.AddDays(-2)), AdapterId = "test", BytesReceived = 100, BytesSent = 50 },
                new DailyUsage { Date = DateOnly.FromDateTime(DateTime.Now.AddDays(-1)), AdapterId = "test", BytesReceived = 200, BytesSent = 100 },
                new DailyUsage { Date = DateOnly.FromDateTime(DateTime.Now), AdapterId = "test", BytesReceived = 300, BytesSent = 150 }
            );
            await db.SaveChangesAsync();
        }

        // Act
        var result = await _service.GetDailyUsageAsync(
            DateOnly.FromDateTime(DateTime.Now.AddDays(-3)),
            DateOnly.FromDateTime(DateTime.Now));

        // Assert
        result.Should().HaveCount(3);
        result[0].BytesReceived.Should().Be(100);
        result[1].BytesReceived.Should().Be(200);
        result[2].BytesReceived.Should().Be(300);
    }

    [Test, Timeout(30000)]
    public async Task GetDailyUsageAsync_FiltersByDateRange(CancellationToken cancellationToken)
    {
        // Arrange - Seed data across multiple days
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();
            db.DailyUsages.AddRange(
                new DailyUsage { Date = DateOnly.FromDateTime(DateTime.Now.AddDays(-10)), AdapterId = "test", BytesReceived = 100 },
                new DailyUsage { Date = DateOnly.FromDateTime(DateTime.Now.AddDays(-5)), AdapterId = "test", BytesReceived = 200 },
                new DailyUsage { Date = DateOnly.FromDateTime(DateTime.Now), AdapterId = "test", BytesReceived = 300 }
            );
            await db.SaveChangesAsync();
        }

        // Act
        var result = await _service.GetDailyUsageAsync(
            DateOnly.FromDateTime(DateTime.Now.AddDays(-6)),
            DateOnly.FromDateTime(DateTime.Now.AddDays(-4)));

        // Assert
        result.Should().HaveCount(1);
        result[0].BytesReceived.Should().Be(200);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GetHourlyUsageAsync Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test, Timeout(30000)]
    public async Task GetHourlyUsageAsync_NoData_ReturnsEmptyList(CancellationToken cancellationToken)
    {
        // Act
        var result = await _service.GetHourlyUsageAsync(DateOnly.FromDateTime(DateTime.Now));

        // Assert
        result.Should().BeEmpty();
    }

    [Test, Timeout(30000)]
    public async Task GetHourlyUsageAsync_ReturnsOnlyRequestedDay(CancellationToken cancellationToken)
    {
        // Arrange
        var today = DateTime.Now;
        var yesterday = DateTime.Now.AddDays(-1);

        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();
            db.HourlyUsages.AddRange(
                new HourlyUsage { Hour = new DateTime(yesterday.Year, yesterday.Month, yesterday.Day, 10, 0, 0), AdapterId = "test", BytesReceived = 100 },
                new HourlyUsage { Hour = new DateTime(today.Year, today.Month, today.Day, 10, 0, 0), AdapterId = "test", BytesReceived = 200 },
                new HourlyUsage { Hour = new DateTime(today.Year, today.Month, today.Day, 14, 0, 0), AdapterId = "test", BytesReceived = 300 }
            );
            await db.SaveChangesAsync();
        }

        // Act
        var result = await _service.GetHourlyUsageAsync(DateOnly.FromDateTime(today));

        // Assert
        result.Should().HaveCount(2);
        result.All(h => h.Hour.Date == today.Date).Should().BeTrue();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GetTotalUsageAsync Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test, Timeout(30000)]
    public async Task GetTotalUsageAsync_NoData_ReturnsZeros(CancellationToken cancellationToken)
    {
        // Act
        var (received, sent) = await _service.GetTotalUsageAsync();

        // Assert
        received.Should().Be(0);
        sent.Should().Be(0);
    }

    [Test, Timeout(30000)]
    public async Task GetTotalUsageAsync_SumsAllDailyRecords(CancellationToken cancellationToken)
    {
        // Arrange
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();
            db.DailyUsages.AddRange(
                new DailyUsage { Date = DateOnly.FromDateTime(DateTime.Now.AddDays(-2)), AdapterId = "test", BytesReceived = 1000, BytesSent = 500 },
                new DailyUsage { Date = DateOnly.FromDateTime(DateTime.Now.AddDays(-1)), AdapterId = "test", BytesReceived = 2000, BytesSent = 1000 },
                new DailyUsage { Date = DateOnly.FromDateTime(DateTime.Now), AdapterId = "test", BytesReceived = 3000, BytesSent = 1500 }
            );
            await db.SaveChangesAsync();
        }

        // Act
        var (received, sent) = await _service.GetTotalUsageAsync();

        // Assert
        received.Should().Be(6000);
        sent.Should().Be(3000);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GetTodayUsageAsync Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test, Timeout(30000)]
    public async Task GetTodayUsageAsync_NoData_ReturnsZeros(CancellationToken cancellationToken)
    {
        // Act
        var (received, sent) = await _service.GetTodayUsageAsync();

        // Assert
        received.Should().Be(0);
        sent.Should().Be(0);
    }

    [Test, Timeout(30000)]
    public async Task GetTodayUsageAsync_OnlyReturnsTodaysData(CancellationToken cancellationToken)
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.Now);
        var yesterday = DateOnly.FromDateTime(DateTime.Now.AddDays(-1));

        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();
            db.DailyUsages.AddRange(
                new DailyUsage { Date = yesterday, AdapterId = "test", BytesReceived = 1000, BytesSent = 500 },
                new DailyUsage { Date = today, AdapterId = "test", BytesReceived = 2000, BytesSent = 1000 }
            );
            await db.SaveChangesAsync();
        }

        // Act
        var (received, sent) = await _service.GetTodayUsageAsync();

        // Assert
        received.Should().Be(2000);
        sent.Should().Be(1000);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Counter Reset / Negative Delta Guard Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test, Timeout(30000)]
    public async Task SaveStatsAsync_CounterReset_UseNewValueAsDeltaInsteadOfNegative(CancellationToken cancellationToken)
    {
        // Arrange — first save with high session counters
        var stats1 = CreateNetworkStats(sessionReceived: 5000, sessionSent: 3000);

        // Simulate a process restart: session counters drop back to a lower value
        var stats2 = CreateNetworkStats(sessionReceived: 1000, sessionSent: 600);

        // Act
        await _service.SaveStatsAsync(stats1);
        await _service.SaveStatsAsync(stats2);

        // Assert — hourly record should have 5000 + 1000 = 6000 (not 5000 + (-4000))
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var hourly = await db.HourlyUsages.FirstAsync();
        hourly.BytesReceived.Should().Be(6000);
        hourly.BytesSent.Should().Be(3600);

        var daily = await db.DailyUsages.FirstAsync();
        daily.BytesReceived.Should().Be(6000);
        daily.BytesSent.Should().Be(3600);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Settings Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test, Timeout(30000)]
    public async Task GetSettingsAsync_NoSettings_ReturnsDefaults(CancellationToken cancellationToken)
    {
        // Act
        var settings = await _service.GetSettingsAsync();

        // Assert
        settings.Should().NotBeNull();
        settings.PollingIntervalMs.Should().Be(1000); // Default value
    }

    [Test, Timeout(30000)]
    public async Task SaveSettingsAsync_NewSettings_CreatesRecord(CancellationToken cancellationToken)
    {
        // Arrange
        var settings = new AppSettings
        {
            PollingIntervalMs = 2000,
            MinimizeToTray = true,
            StartMinimized = false
        };

        // Act
        await _service.SaveSettingsAsync(settings);
        var loaded = await _service.GetSettingsAsync();

        // Assert
        loaded.PollingIntervalMs.Should().Be(2000);
        loaded.MinimizeToTray.Should().BeTrue();
        loaded.StartMinimized.Should().BeFalse();
    }

    [Test, Timeout(30000)]
    public async Task SaveSettingsAsync_ExistingSettings_UpdatesRecord(CancellationToken cancellationToken)
    {
        // Arrange - Create initial settings
        var settings1 = new AppSettings { PollingIntervalMs = 1000 };
        await _service.SaveSettingsAsync(settings1);

        // Act - Update settings
        var settings2 = new AppSettings { PollingIntervalMs = 5000 };
        await _service.SaveSettingsAsync(settings2);

        // Assert - Should have only one record with updated value
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var count = await db.Settings.CountAsync();
        count.Should().Be(1);

        var loaded = await _service.GetSettingsAsync();
        loaded.PollingIntervalMs.Should().Be(5000);
    }

    [Test, Timeout(30000)]
    public async Task SaveSettingsAsync_AllFields_RoundTripCorrectly(CancellationToken cancellationToken)
    {
        // Arrange — set every field to a non-default value
        var settings = new AppSettings
        {
            PollingIntervalMs = 3000,
            SaveIntervalSeconds = 120,
            StartWithWindows = true,
            MinimizeToTray = false,
            StartMinimized = true,
            UseIpHelperApi = true,
            SelectedAdapterId = "eth0-custom",
            DataRetentionDays = 180,
            Theme = "Light",
            SpeedUnit = SpeedUnit.BitsPerSecond,
            IsPerAppTrackingEnabled = true,
            AppDataRetentionDays = 30,
            AppDataAggregateAfterDays = 14,
            ShowSystemMetricsInHeader = false,
            ShowCpuOverlayByDefault = true,
            ShowMemoryOverlayByDefault = true,
            ShowGpuMetrics = false,
            DefaultTimeRange = "OneHour",
            PerformanceModeEnabled = true,
            ChartUpdateIntervalMs = 2500,
            DefaultInsightsPeriod = "ThisMonth",
            ShowCorrelationInsights = false,
            CheckForUpdates = false,
            AutoDownloadUpdates = false
        };

        // Act
        await _service.SaveSettingsAsync(settings);
        var loaded = await _service.GetSettingsAsync();

        // Assert — verify every field
        loaded.PollingIntervalMs.Should().Be(3000);
        loaded.SaveIntervalSeconds.Should().Be(120);
        loaded.StartWithWindows.Should().BeTrue();
        loaded.MinimizeToTray.Should().BeFalse();
        loaded.StartMinimized.Should().BeTrue();
        loaded.UseIpHelperApi.Should().BeTrue();
        loaded.SelectedAdapterId.Should().Be("eth0-custom");
        loaded.DataRetentionDays.Should().Be(180);
        loaded.Theme.Should().Be("Light");
        loaded.SpeedUnit.Should().Be(SpeedUnit.BitsPerSecond);
        loaded.IsPerAppTrackingEnabled.Should().BeTrue();
        loaded.AppDataRetentionDays.Should().Be(30);
        loaded.AppDataAggregateAfterDays.Should().Be(14);
        loaded.ShowSystemMetricsInHeader.Should().BeFalse();
        loaded.ShowCpuOverlayByDefault.Should().BeTrue();
        loaded.ShowMemoryOverlayByDefault.Should().BeTrue();
        loaded.ShowGpuMetrics.Should().BeFalse();
        loaded.DefaultTimeRange.Should().Be("OneHour");
        loaded.PerformanceModeEnabled.Should().BeTrue();
        loaded.ChartUpdateIntervalMs.Should().Be(2500);
        loaded.DefaultInsightsPeriod.Should().Be("ThisMonth");
        loaded.ShowCorrelationInsights.Should().BeFalse();
        loaded.CheckForUpdates.Should().BeFalse();
        loaded.AutoDownloadUpdates.Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Speed Snapshot Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test, Timeout(30000)]
    public async Task SaveSpeedSnapshotAsync_SavesCorrectly(CancellationToken cancellationToken)
    {
        // Act
        await _service.SaveSpeedSnapshotAsync(1_000_000, 500_000);

        // Assert
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var snapshot = await db.SpeedSnapshots.FirstOrDefaultAsync();
        snapshot.Should().NotBeNull();
        snapshot!.DownloadSpeedBps.Should().Be(1_000_000);
        snapshot.UploadSpeedBps.Should().Be(500_000);
    }

    [Test, Timeout(30000)]
    public async Task SaveSpeedSnapshotBatchAsync_SavesMultipleSnapshots(CancellationToken cancellationToken)
    {
        // Arrange
        var now = DateTime.Now;
        var snapshots = new List<(long, long, DateTime)>
        {
            (100_000, 50_000, now.AddSeconds(-2)),
            (200_000, 100_000, now.AddSeconds(-1)),
            (300_000, 150_000, now)
        };

        // Act
        await _service.SaveSpeedSnapshotBatchAsync(snapshots);

        // Assert
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var count = await db.SpeedSnapshots.CountAsync();
        count.Should().Be(3);
    }

    [Test, Timeout(30000)]
    public async Task SaveSpeedSnapshotBatchAsync_EmptyList_DoesNothing(CancellationToken cancellationToken)
    {
        // Arrange
        var snapshots = new List<(long, long, DateTime)>();

        // Act
        await _service.SaveSpeedSnapshotBatchAsync(snapshots);

        // Assert
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var count = await db.SpeedSnapshots.CountAsync();
        count.Should().Be(0);
    }

    [Test, Timeout(30000)]
    public async Task GetSpeedHistoryAsync_ReturnsOrderedSnapshots(CancellationToken cancellationToken)
    {
        // Arrange
        var now = DateTime.Now;
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();
            db.SpeedSnapshots.AddRange(
                new SpeedSnapshot { Timestamp = now.AddMinutes(-2), DownloadSpeedBps = 100, UploadSpeedBps = 50 },
                new SpeedSnapshot { Timestamp = now.AddMinutes(-1), DownloadSpeedBps = 200, UploadSpeedBps = 100 },
                new SpeedSnapshot { Timestamp = now, DownloadSpeedBps = 300, UploadSpeedBps = 150 }
            );
            await db.SaveChangesAsync();
        }

        // Act
        var result = await _service.GetSpeedHistoryAsync(now.AddMinutes(-5));

        // Assert
        result.Should().HaveCount(3);
        result[0].DownloadSpeedBps.Should().Be(100);
        result[1].DownloadSpeedBps.Should().Be(200);
        result[2].DownloadSpeedBps.Should().Be(300);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SaveAppStatsAsync Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test, Timeout(30000)]
    public async Task SaveAppStatsAsync_WithValidStats_PersistsAppUsage(CancellationToken cancellationToken)
    {
        // Arrange
        var stats = new List<ProcessNetworkStats>
        {
            new()
            {
                AppIdentifier = "app-hash-1",
                DisplayName = "Chrome",
                ProcessName = "chrome",
                ExecutablePath = @"C:\chrome.exe",
                SessionBytesReceived = 5000,
                SessionBytesSent = 2000,
                DownloadSpeedBps = 100_000,
                UploadSpeedBps = 50_000
            },
            new()
            {
                AppIdentifier = "app-hash-2",
                DisplayName = "Firefox",
                ProcessName = "firefox",
                ExecutablePath = @"C:\firefox.exe",
                SessionBytesReceived = 3000,
                SessionBytesSent = 1000,
                DownloadSpeedBps = 80_000,
                UploadSpeedBps = 40_000
            }
        };

        // Act
        await _service.SaveAppStatsAsync(stats);

        // Assert — verify via GetTopAppsAsync
        var today = DateOnly.FromDateTime(DateTime.Now);
        var topApps = await _service.GetTopAppsAsync(10, today, today);

        topApps.Should().HaveCount(2);
        var chrome = topApps.First(a => a.AppIdentifier == "app-hash-1");
        chrome.AppName.Should().Be("Chrome");
        chrome.BytesReceived.Should().Be(5000);
        chrome.BytesSent.Should().Be(2000);
    }

    [Test, Timeout(30000)]
    public async Task SaveAppStatsAsync_WithNullAppIdentifier_FiltersOutInvalidEntries(CancellationToken cancellationToken)
    {
        // Arrange
        var stats = new List<ProcessNetworkStats>
        {
            new()
            {
                AppIdentifier = "",
                DisplayName = "Invalid",
                SessionBytesReceived = 1000,
                SessionBytesSent = 500
            },
            new()
            {
                AppIdentifier = "valid-hash",
                DisplayName = "ValidApp",
                ProcessName = "valid",
                ExecutablePath = @"C:\valid.exe",
                SessionBytesReceived = 2000,
                SessionBytesSent = 1000,
                DownloadSpeedBps = 50_000,
                UploadSpeedBps = 25_000
            }
        };

        // Act
        await _service.SaveAppStatsAsync(stats);

        // Assert — only the valid entry should be persisted
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var records = await db.AppUsageRecords.ToListAsync();
        records.Should().HaveCount(1);
        records[0].AppIdentifier.Should().Be("valid-hash");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GetTodayUsageByAdapterAsync Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test, Timeout(30000)]
    public async Task GetTodayUsageByAdapterAsync_ReturnsPerAdapterTotals(CancellationToken cancellationToken)
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.Now);
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();
            db.DailyUsages.AddRange(
                new DailyUsage { Date = today, AdapterId = "eth0", BytesReceived = 1000, BytesSent = 500 },
                new DailyUsage { Date = today, AdapterId = "wlan0", BytesReceived = 2000, BytesSent = 1000 }
            );
            await db.SaveChangesAsync();
        }

        // Act
        var result = await _service.GetTodayUsageByAdapterAsync();

        // Assert
        result.Should().HaveCount(2);
        result["eth0"].received.Should().Be(1000);
        result["eth0"].sent.Should().Be(500);
        result["wlan0"].received.Should().Be(2000);
        result["wlan0"].sent.Should().Be(1000);
    }

    [Test, Timeout(30000)]
    public async Task GetTodayUsageByAdapterAsync_WithNoData_ReturnsEmptyDictionary(CancellationToken cancellationToken)
    {
        // Act
        var result = await _service.GetTodayUsageByAdapterAsync();

        // Assert
        result.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CleanupOldAppDataAsync Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test, Timeout(30000)]
    public async Task CleanupOldAppDataAsync_WithZeroRetentionDays_IsNoOp(CancellationToken cancellationToken)
    {
        // Arrange
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();
            db.AppUsageRecords.Add(new AppUsageRecord
            {
                AppIdentifier = "some-app",
                AppName = "SomeApp",
                Timestamp = DateTime.Now.AddDays(-100),
                Granularity = UsageGranularity.Daily,
                BytesReceived = 100,
                BytesSent = 50,
                LastUpdated = DateTime.Now
            });
            await db.SaveChangesAsync();
        }

        // Act
        await _service.CleanupOldAppDataAsync(0);

        // Assert — record should still exist
        using var verifyScope = CreateScope();
        var db2 = verifyScope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var count = await db2.AppUsageRecords.CountAsync();
        count.Should().Be(1);
    }
}
