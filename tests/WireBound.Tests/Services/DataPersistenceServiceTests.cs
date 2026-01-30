using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WireBound.Avalonia.Services;
using WireBound.Core.Data;
using WireBound.Core.Models;
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

    // Note: This test is skipped because ExecuteDeleteAsync is not supported by EF Core InMemory provider.
    // The actual functionality works correctly with SQLite in production.
    [Test, Skip("ExecuteDeleteAsync not supported by InMemory provider"), Timeout(30000)]
    public async Task CleanupOldSpeedSnapshotsAsync_RemovesOldSnapshots(CancellationToken cancellationToken)
    {
        // Arrange
        var now = DateTime.Now;
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();
            db.SpeedSnapshots.AddRange(
                new SpeedSnapshot { Timestamp = now.AddHours(-3), DownloadSpeedBps = 100, UploadSpeedBps = 50 },
                new SpeedSnapshot { Timestamp = now.AddHours(-1), DownloadSpeedBps = 200, UploadSpeedBps = 100 },
                new SpeedSnapshot { Timestamp = now, DownloadSpeedBps = 300, UploadSpeedBps = 150 }
            );
            await db.SaveChangesAsync();
        }

        // Act
        await _service.CleanupOldSpeedSnapshotsAsync(TimeSpan.FromHours(2));

        // Assert
        using var verifyScope = CreateScope();
        var db2 = verifyScope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var remaining = await db2.SpeedSnapshots.ToListAsync();
        remaining.Should().HaveCount(2);
        remaining.All(s => s.Timestamp > now.AddHours(-2)).Should().BeTrue();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Cleanup Tests
    // ═══════════════════════════════════════════════════════════════════════

    // Note: This test is skipped because ExecuteDeleteAsync is not supported by EF Core InMemory provider.
    // The actual functionality works correctly with SQLite in production.
    [Test, Skip("ExecuteDeleteAsync not supported by InMemory provider"), Timeout(30000)]
    public async Task CleanupOldDataAsync_RemovesOldRecords(CancellationToken cancellationToken)
    {
        // Arrange
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

            // Add daily records
            db.DailyUsages.AddRange(
                new DailyUsage { Date = DateOnly.FromDateTime(DateTime.Now.AddDays(-100)), AdapterId = "test", BytesReceived = 100 },
                new DailyUsage { Date = DateOnly.FromDateTime(DateTime.Now.AddDays(-50)), AdapterId = "test", BytesReceived = 200 },
                new DailyUsage { Date = DateOnly.FromDateTime(DateTime.Now), AdapterId = "test", BytesReceived = 300 }
            );

            // Add hourly records
            db.HourlyUsages.AddRange(
                new HourlyUsage { Hour = DateTime.Now.AddDays(-100), AdapterId = "test", BytesReceived = 100 },
                new HourlyUsage { Hour = DateTime.Now.AddDays(-50), AdapterId = "test", BytesReceived = 200 },
                new HourlyUsage { Hour = DateTime.Now, AdapterId = "test", BytesReceived = 300 }
            );

            await db.SaveChangesAsync();
        }

        // Act
        await _service.CleanupOldDataAsync(60); // Keep last 60 days

        // Assert
        using var verifyScope = CreateScope();
        var db2 = verifyScope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var dailyCount = await db2.DailyUsages.CountAsync();
        var hourlyCount = await db2.HourlyUsages.CountAsync();

        dailyCount.Should().Be(2); // -50 and today
        hourlyCount.Should().Be(2); // -50 and today
    }
}
