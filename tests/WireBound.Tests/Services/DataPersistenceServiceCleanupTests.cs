using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WireBound.Avalonia.Services;
using WireBound.Core.Data;
using WireBound.Core.Models;
using WireBound.Tests.Fixtures;

namespace WireBound.Tests.Services;

/// <summary>
/// Cleanup tests for DataPersistenceService that require SQLite in-memory
/// because ExecuteDeleteAsync is not supported by the EF Core InMemory provider.
/// </summary>
public class DataPersistenceServiceCleanupTests : SqliteDatabaseTestBase
{
    private readonly DataPersistenceService _service;

    public DataPersistenceServiceCleanupTests()
    {
        _service = new DataPersistenceService(ServiceProvider);
    }

    [Test, Timeout(30000)]
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

    [Test, Timeout(30000)]
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

    [Test, Timeout(30000)]
    public async Task CleanupOldAppDataAsync_RemovesOldRecords(CancellationToken cancellationToken)
    {
        // Arrange
        var now = DateTime.Now;
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();
            db.AppUsageRecords.AddRange(
                new AppUsageRecord
                {
                    AppIdentifier = "old-app",
                    AppName = "OldApp",
                    Timestamp = now.AddDays(-60),
                    Granularity = UsageGranularity.Daily,
                    BytesReceived = 100,
                    BytesSent = 50,
                    LastUpdated = now.AddDays(-60)
                },
                new AppUsageRecord
                {
                    AppIdentifier = "recent-app",
                    AppName = "RecentApp",
                    Timestamp = now.AddDays(-10),
                    Granularity = UsageGranularity.Daily,
                    BytesReceived = 200,
                    BytesSent = 100,
                    LastUpdated = now.AddDays(-10)
                }
            );
            await db.SaveChangesAsync();
        }

        // Act
        await _service.CleanupOldAppDataAsync(30); // Keep last 30 days

        // Assert
        using var verifyScope = CreateScope();
        var db2 = verifyScope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var remaining = await db2.AppUsageRecords.ToListAsync();
        remaining.Should().HaveCount(1);
        remaining[0].AppIdentifier.Should().Be("recent-app");
    }
}
