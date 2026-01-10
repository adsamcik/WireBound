using Microsoft.EntityFrameworkCore;
using WireBound.Models;
using WireBound.Tests.TestInfrastructure;

namespace WireBound.Tests.Services;

/// <summary>
/// Tests for DataPersistenceService using in-memory database
/// </summary>
public class DataPersistenceServiceTests
{
    private static TestableDbContext CreateDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<TestableDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var context = new TestableDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    #region SaveStatsAsync Tests

    [Test]
    public async Task SaveStatsAsync_CreatesNewHourlyRecord_WhenNoneExists()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        using var db = CreateDbContext(dbName);
        var service = new TestableDataPersistenceService(db);

        var stats = new NetworkStats
        {
            AdapterId = "eth0",
            DownloadSpeedBps = 1_000_000,
            UploadSpeedBps = 500_000,
            SessionBytesReceived = 100_000,
            SessionBytesSent = 50_000
        };

        // Act
        await service.SaveStatsAsync(stats);

        // Assert
        var hourlyRecords = await db.HourlyUsages.ToListAsync();
        
        await Assert.That(hourlyRecords.Count).IsEqualTo(1);
        await Assert.That(hourlyRecords[0].AdapterId).IsEqualTo("eth0");
    }

    [Test]
    public async Task SaveStatsAsync_CreatesNewDailyRecord_WhenNoneExists()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        using var db = CreateDbContext(dbName);
        var service = new TestableDataPersistenceService(db);

        var stats = new NetworkStats
        {
            AdapterId = "eth0",
            SessionBytesReceived = 200_000,
            SessionBytesSent = 100_000
        };

        // Act
        await service.SaveStatsAsync(stats);

        // Assert
        var dailyRecords = await db.DailyUsages.ToListAsync();
        
        await Assert.That(dailyRecords.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SaveStatsAsync_UpdatesExistingHourlyRecord_WhenExists()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        using var db = CreateDbContext(dbName);
        var service = new TestableDataPersistenceService(db);

        // First save
        var stats1 = new NetworkStats
        {
            AdapterId = "eth0",
            SessionBytesReceived = 100_000,
            SessionBytesSent = 50_000,
            DownloadSpeedBps = 1_000_000,
            UploadSpeedBps = 500_000
        };
        await service.SaveStatsAsync(stats1);

        // Second save with more data
        var stats2 = new NetworkStats
        {
            AdapterId = "eth0",
            SessionBytesReceived = 200_000,
            SessionBytesSent = 100_000,
            DownloadSpeedBps = 2_000_000,
            UploadSpeedBps = 1_000_000
        };
        await service.SaveStatsAsync(stats2);

        // Assert
        var hourlyRecords = await db.HourlyUsages.ToListAsync();
        
        await Assert.That(hourlyRecords.Count).IsEqualTo(1);
        // Delta from first save: 100k + delta from second save: (200k - 100k) = 200k total
        await Assert.That(hourlyRecords[0].BytesReceived).IsEqualTo(200_000);
    }

    [Test]
    public async Task SaveStatsAsync_TracksPeakDownloadSpeed()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        using var db = CreateDbContext(dbName);
        var service = new TestableDataPersistenceService(db);

        // First save with lower speed
        await service.SaveStatsAsync(new NetworkStats
        {
            AdapterId = "eth0",
            SessionBytesReceived = 100_000,
            DownloadSpeedBps = 1_000_000
        });

        // Second save with higher speed
        await service.SaveStatsAsync(new NetworkStats
        {
            AdapterId = "eth0",
            SessionBytesReceived = 200_000,
            DownloadSpeedBps = 5_000_000
        });

        // Third save with lower speed
        await service.SaveStatsAsync(new NetworkStats
        {
            AdapterId = "eth0",
            SessionBytesReceived = 300_000,
            DownloadSpeedBps = 2_000_000
        });

        // Assert
        var hourlyRecord = await db.HourlyUsages.FirstAsync();
        
        await Assert.That(hourlyRecord.PeakDownloadSpeed).IsEqualTo(5_000_000);
    }

    #endregion

    #region GetDailyUsageAsync Tests

    [Test]
    public async Task GetDailyUsageAsync_ReturnsRecordsInDateRange()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        using var db = CreateDbContext(dbName);
        
        db.DailyUsages.AddRange(
            new DailyUsage { Date = new DateOnly(2026, 1, 5), AdapterId = "eth0", BytesReceived = 1000 },
            new DailyUsage { Date = new DateOnly(2026, 1, 10), AdapterId = "eth0", BytesReceived = 2000 },
            new DailyUsage { Date = new DateOnly(2026, 1, 15), AdapterId = "eth0", BytesReceived = 3000 },
            new DailyUsage { Date = new DateOnly(2026, 1, 20), AdapterId = "eth0", BytesReceived = 4000 }
        );
        await db.SaveChangesAsync();

        var service = new TestableDataPersistenceService(db);

        // Act
        var results = await service.GetDailyUsageAsync(
            new DateOnly(2026, 1, 8), 
            new DateOnly(2026, 1, 18));

        // Assert
        await Assert.That(results.Count).IsEqualTo(2);
        await Assert.That(results[0].Date).IsEqualTo(new DateOnly(2026, 1, 10));
        await Assert.That(results[1].Date).IsEqualTo(new DateOnly(2026, 1, 15));
    }

    [Test]
    public async Task GetDailyUsageAsync_ReturnsEmptyList_WhenNoDataInRange()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        using var db = CreateDbContext(dbName);
        var service = new TestableDataPersistenceService(db);

        // Act
        var results = await service.GetDailyUsageAsync(
            new DateOnly(2026, 1, 1), 
            new DateOnly(2026, 1, 31));

        // Assert
        await Assert.That(results).IsEmpty();
    }

    [Test]
    public async Task GetDailyUsageAsync_ReturnsResultsOrderedByDate()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        using var db = CreateDbContext(dbName);
        
        // Add in random order (use different adapter IDs to avoid unique constraint)
        db.DailyUsages.AddRange(
            new DailyUsage { Date = new DateOnly(2026, 1, 15), AdapterId = "eth0" },
            new DailyUsage { Date = new DateOnly(2026, 1, 5), AdapterId = "eth1" },
            new DailyUsage { Date = new DateOnly(2026, 1, 10), AdapterId = "eth2" }
        );
        await db.SaveChangesAsync();

        var service = new TestableDataPersistenceService(db);

        // Act
        var results = await service.GetDailyUsageAsync(
            new DateOnly(2026, 1, 1), 
            new DateOnly(2026, 1, 31));

        // Assert
        await Assert.That(results[0].Date).IsEqualTo(new DateOnly(2026, 1, 5));
        await Assert.That(results[1].Date).IsEqualTo(new DateOnly(2026, 1, 10));
        await Assert.That(results[2].Date).IsEqualTo(new DateOnly(2026, 1, 15));
    }

    #endregion

    #region GetHourlyUsageAsync Tests

    [Test]
    public async Task GetHourlyUsageAsync_ReturnsRecordsForSpecificDate()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        using var db = CreateDbContext(dbName);
        
        db.HourlyUsages.AddRange(
            new HourlyUsage { Hour = new DateTime(2026, 1, 9, 23, 0, 0), AdapterId = "eth0" },
            new HourlyUsage { Hour = new DateTime(2026, 1, 10, 8, 0, 0), AdapterId = "eth0" },
            new HourlyUsage { Hour = new DateTime(2026, 1, 10, 14, 0, 0), AdapterId = "eth1" },
            new HourlyUsage { Hour = new DateTime(2026, 1, 11, 1, 0, 0), AdapterId = "eth0" }
        );
        await db.SaveChangesAsync();

        var service = new TestableDataPersistenceService(db);

        // Act
        var results = await service.GetHourlyUsageAsync(new DateOnly(2026, 1, 10));

        // Assert
        await Assert.That(results.Count).IsEqualTo(2);
    }

    [Test]
    public async Task GetHourlyUsageAsync_ReturnsResultsOrderedByHour()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        using var db = CreateDbContext(dbName);
        
        // Add in random order (use different adapter IDs to avoid unique constraint)
        db.HourlyUsages.AddRange(
            new HourlyUsage { Hour = new DateTime(2026, 1, 10, 18, 0, 0), AdapterId = "eth0" },
            new HourlyUsage { Hour = new DateTime(2026, 1, 10, 6, 0, 0), AdapterId = "eth1" },
            new HourlyUsage { Hour = new DateTime(2026, 1, 10, 12, 0, 0), AdapterId = "eth2" }
        );
        await db.SaveChangesAsync();

        var service = new TestableDataPersistenceService(db);

        // Act
        var results = await service.GetHourlyUsageAsync(new DateOnly(2026, 1, 10));

        // Assert
        await Assert.That(results[0].Hour.Hour).IsEqualTo(6);
        await Assert.That(results[1].Hour.Hour).IsEqualTo(12);
        await Assert.That(results[2].Hour.Hour).IsEqualTo(18);
    }

    #endregion

    #region GetTotalUsageAsync Tests

    [Test]
    public async Task GetTotalUsageAsync_ReturnsZero_WhenNoData()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        using var db = CreateDbContext(dbName);
        var service = new TestableDataPersistenceService(db);

        // Act
        var (totalReceived, totalSent) = await service.GetTotalUsageAsync();

        // Assert
        await Assert.That(totalReceived).IsEqualTo(0);
        await Assert.That(totalSent).IsEqualTo(0);
    }

    [Test]
    public async Task GetTotalUsageAsync_SumsAllDailyRecords()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        using var db = CreateDbContext(dbName);
        
        db.DailyUsages.AddRange(
            new DailyUsage { Date = new DateOnly(2026, 1, 1), AdapterId = "eth0", BytesReceived = 1_000_000, BytesSent = 500_000 },
            new DailyUsage { Date = new DateOnly(2026, 1, 2), AdapterId = "eth1", BytesReceived = 2_000_000, BytesSent = 1_000_000 },
            new DailyUsage { Date = new DateOnly(2026, 1, 3), AdapterId = "eth2", BytesReceived = 3_000_000, BytesSent = 1_500_000 }
        );
        await db.SaveChangesAsync();

        var service = new TestableDataPersistenceService(db);

        // Act
        var (totalReceived, totalSent) = await service.GetTotalUsageAsync();

        // Assert
        await Assert.That(totalReceived).IsEqualTo(6_000_000);
        await Assert.That(totalSent).IsEqualTo(3_000_000);
    }

    #endregion

    #region CleanupOldDataAsync Tests

    [Test]
    public async Task CleanupOldDataAsync_RemovesOldDailyRecords()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        using var db = CreateDbContext(dbName);
        
        var today = DateOnly.FromDateTime(DateTime.Now);
        db.DailyUsages.AddRange(
            new DailyUsage { Date = today.AddDays(-100), AdapterId = "eth0" }, // Old
            new DailyUsage { Date = today.AddDays(-50), AdapterId = "eth1" },  // Old
            new DailyUsage { Date = today.AddDays(-10), AdapterId = "eth2" },  // Recent
            new DailyUsage { Date = today, AdapterId = "eth3" }                 // Today
        );
        await db.SaveChangesAsync();

        var service = new TestableDataPersistenceService(db);

        // Act
        await service.CleanupOldDataAsync(30); // Keep last 30 days

        // Assert
        var remainingRecords = await db.DailyUsages.ToListAsync();
        await Assert.That(remainingRecords.Count).IsEqualTo(2);
    }

    [Test]
    public async Task CleanupOldDataAsync_RemovesOldHourlyRecords()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        using var db = CreateDbContext(dbName);
        
        var now = DateTime.Now;
        db.HourlyUsages.AddRange(
            new HourlyUsage { Hour = now.AddDays(-100), AdapterId = "eth0" }, // Old
            new HourlyUsage { Hour = now.AddDays(-5), AdapterId = "eth1" },   // Recent
            new HourlyUsage { Hour = now.AddHours(-2), AdapterId = "eth2" }   // Recent
        );
        await db.SaveChangesAsync();

        var service = new TestableDataPersistenceService(db);

        // Act
        await service.CleanupOldDataAsync(30);

        // Assert
        var remainingRecords = await db.HourlyUsages.ToListAsync();
        await Assert.That(remainingRecords.Count).IsEqualTo(2);
    }

    #endregion

    #region Settings Tests

    [Test]
    public async Task GetSettingsAsync_ReturnsDefaultSettings_WhenNoneExist()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        using var db = CreateDbContext(dbName);
        var service = new TestableDataPersistenceService(db);

        // Act
        var settings = await service.GetSettingsAsync();

        // Assert
        await Assert.That(settings).IsNotNull();
        await Assert.That(settings.PollingIntervalMs).IsEqualTo(1000);
        await Assert.That(settings.DataRetentionDays).IsEqualTo(365);
    }

    [Test]
    public async Task SaveSettingsAsync_CreatesNewSettings_WhenNoneExist()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        using var db = CreateDbContext(dbName);
        var service = new TestableDataPersistenceService(db);

        var settings = new AppSettings
        {
            PollingIntervalMs = 500,
            SaveIntervalSeconds = 30,
            StartWithWindows = true
        };

        // Act
        await service.SaveSettingsAsync(settings);

        // Assert
        var savedSettings = await db.Settings.FirstOrDefaultAsync();
        
        await Assert.That(savedSettings).IsNotNull();
        await Assert.That(savedSettings!.PollingIntervalMs).IsEqualTo(500);
        await Assert.That(savedSettings.StartWithWindows).IsTrue();
    }

    [Test]
    public async Task SaveSettingsAsync_UpdatesExistingSettings()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        using var db = CreateDbContext(dbName);
        
        // Create initial settings
        db.Settings.Add(new AppSettings { Id = 1, PollingIntervalMs = 1000 });
        await db.SaveChangesAsync();

        var service = new TestableDataPersistenceService(db);

        // Act
        var updatedSettings = new AppSettings
        {
            Id = 1,
            PollingIntervalMs = 250,
            UseIpHelperApi = true
        };
        await service.SaveSettingsAsync(updatedSettings);

        // Assert
        var settings = await db.Settings.FirstAsync();
        await Assert.That(settings.PollingIntervalMs).IsEqualTo(250);
        await Assert.That(settings.UseIpHelperApi).IsTrue();
    }

    #endregion
}
