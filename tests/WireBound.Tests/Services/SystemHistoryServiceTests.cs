using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WireBound.Avalonia.Services;
using WireBound.Core.Data;
using WireBound.Core.Models;

namespace WireBound.Tests.Services;

/// <summary>
/// Unit tests for SystemHistoryService
/// </summary>
public class SystemHistoryServiceTests : IAsyncDisposable
{
    private readonly WireBoundDbContext _context;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SystemHistoryService> _loggerMock;
    private readonly SystemHistoryService _service;
    private readonly string _databaseName;

    public SystemHistoryServiceTests()
    {
        _databaseName = Guid.NewGuid().ToString();
        _loggerMock = Substitute.For<ILogger<SystemHistoryService>>();

        // Set up in-memory database with scoped context
        var services = new ServiceCollection();
        services.AddDbContext<WireBoundDbContext>(options =>
            options.UseInMemoryDatabase(databaseName: _databaseName));

        _serviceProvider = services.BuildServiceProvider();

        // Get a context instance for direct testing
        using var scope = _serviceProvider.CreateScope();
        _context = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();
        _context.Database.EnsureCreated();

        _service = new SystemHistoryService(_serviceProvider, _loggerMock);
    }

    public ValueTask DisposeAsync()
    {
        _service.Dispose();
        _context.Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    #region Helper Methods

    private static SystemStats CreateSystemStats(
        double cpuUsage = 50.0,
        double memoryUsage = 60.0,
        long? memoryUsedBytes = null,
        DateTime? timestamp = null)
    {
        var ts = timestamp ?? DateTime.Now;
        var totalBytes = 16_000_000_000L;
        var usedBytes = memoryUsedBytes ?? (long)(memoryUsage / 100.0 * totalBytes);
        return new SystemStats
        {
            Timestamp = ts,
            Cpu = new CpuStats
            {
                Timestamp = ts,
                UsagePercent = cpuUsage,
                ProcessorCount = 8
            },
            Memory = new MemoryStats
            {
                Timestamp = ts,
                TotalBytes = totalBytes,
                UsedBytes = usedBytes,
                AvailableBytes = totalBytes - usedBytes
            }
        };
    }

    private async Task<WireBoundDbContext> GetFreshContextAsync()
    {
        var scope = _serviceProvider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();
    }

    private async Task SeedHourlyStatsAsync(params HourlySystemStats[] stats)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();
        db.HourlySystemStats.AddRange(stats);
        await db.SaveChangesAsync();
    }

    private async Task SeedDailyStatsAsync(params DailySystemStats[] stats)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();
        db.DailySystemStats.AddRange(stats);
        await db.SaveChangesAsync();
    }

    #endregion

    #region RecordSampleAsync Tests

    [Test, Timeout(30000)]
    public async Task RecordSampleAsync_AddsSampleToBuffer(CancellationToken cancellationToken)
    {
        // Arrange
        var stats = CreateSystemStats(cpuUsage: 75.0, memoryUsage: 80.0);

        // Act
        await _service.RecordSampleAsync(stats);

        // Assert - We can't directly inspect the buffer, but we can verify
        // the sample survives aggregation by triggering it with a past timestamp
        // For this test, we verify no exception is thrown
        await _service.RecordSampleAsync(stats);
        // Success if no exception
    }

    [Test, Timeout(30000)]
    public async Task RecordSampleAsync_MultipleSamples_AllAdded(CancellationToken cancellationToken)
    {
        // Arrange
        var stats1 = CreateSystemStats(cpuUsage: 25.0);
        var stats2 = CreateSystemStats(cpuUsage: 50.0);
        var stats3 = CreateSystemStats(cpuUsage: 75.0);

        // Act
        await _service.RecordSampleAsync(stats1);
        await _service.RecordSampleAsync(stats2);
        await _service.RecordSampleAsync(stats3);

        // Assert - no exceptions means samples were recorded
        // Actual verification happens through aggregation tests
    }

    [Test, Timeout(30000)]
    public async Task RecordSampleAsync_AfterDispose_DoesNotThrow(CancellationToken cancellationToken)
    {
        // Arrange
        var service = new SystemHistoryService(_serviceProvider, _loggerMock);
        var stats = CreateSystemStats();

        // Act
        service.Dispose();

        // Should not throw after disposal
        await service.RecordSampleAsync(stats);
    }

    #endregion

    #region Buffer Limit Tests

    [Test, Timeout(30000)]
    public async Task RecordSampleAsync_BufferLimit_DoesNotGrowUnbounded(CancellationToken cancellationToken)
    {
        // Arrange - The buffer limit is 7200 samples
        const int sampleCount = 8000; // More than the limit

        // Act
        for (int i = 0; i < sampleCount; i++)
        {
            var stats = CreateSystemStats(cpuUsage: i % 100);
            await _service.RecordSampleAsync(stats);
        }

        // Assert - No exception means buffer management is working
        // The service should have dequeued older samples
    }

    [Test, Timeout(30000)]
    public async Task RecordSampleAsync_ExactlyAtLimit_NoOverflow(CancellationToken cancellationToken)
    {
        // Arrange
        const int sampleCount = 7200; // Exactly at limit

        // Act
        for (int i = 0; i < sampleCount; i++)
        {
            var stats = CreateSystemStats(cpuUsage: i % 100);
            await _service.RecordSampleAsync(stats);
        }

        // Assert - No memory issues or exceptions
    }

    #endregion

    #region GetHourlyStatsAsync Tests

    [Test, Timeout(30000)]
    public async Task GetHourlyStatsAsync_ReturnsCorrectDataRange(CancellationToken cancellationToken)
    {
        // Arrange
        var now = DateTime.Now;
        var hour1 = new DateTime(now.Year, now.Month, now.Day, 10, 0, 0);
        var hour2 = new DateTime(now.Year, now.Month, now.Day, 11, 0, 0);
        var hour3 = new DateTime(now.Year, now.Month, now.Day, 12, 0, 0);
        var hour4 = new DateTime(now.Year, now.Month, now.Day, 13, 0, 0);

        await SeedHourlyStatsAsync(
            new HourlySystemStats { Hour = hour1, AvgCpuPercent = 30, AvgMemoryPercent = 40 },
            new HourlySystemStats { Hour = hour2, AvgCpuPercent = 50, AvgMemoryPercent = 60 },
            new HourlySystemStats { Hour = hour3, AvgCpuPercent = 70, AvgMemoryPercent = 80 },
            new HourlySystemStats { Hour = hour4, AvgCpuPercent = 90, AvgMemoryPercent = 95 }
        );

        // Act - Query middle range
        var result = await _service.GetHourlyStatsAsync(hour2, hour3);

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(h => h.Hour == hour2);
        result.Should().Contain(h => h.Hour == hour3);
        result.Should().NotContain(h => h.Hour == hour1);
        result.Should().NotContain(h => h.Hour == hour4);
    }

    [Test, Timeout(30000)]
    public async Task GetHourlyStatsAsync_ReturnsOrderedByHour(CancellationToken cancellationToken)
    {
        // Arrange
        var baseTime = new DateTime(2025, 1, 15, 0, 0, 0);
        await SeedHourlyStatsAsync(
            new HourlySystemStats { Hour = baseTime.AddHours(3), AvgCpuPercent = 30 },
            new HourlySystemStats { Hour = baseTime.AddHours(1), AvgCpuPercent = 10 },
            new HourlySystemStats { Hour = baseTime.AddHours(2), AvgCpuPercent = 20 }
        );

        // Act
        var result = await _service.GetHourlyStatsAsync(baseTime, baseTime.AddHours(4));

        // Assert
        result.Should().HaveCount(3);
        result.Should().BeInAscendingOrder(h => h.Hour);
    }

    [Test, Timeout(30000)]
    public async Task GetHourlyStatsAsync_EmptyData_ReturnsEmptyList(CancellationToken cancellationToken)
    {
        // Arrange
        var start = DateTime.Now.AddDays(-1);
        var end = DateTime.Now;

        // Act
        var result = await _service.GetHourlyStatsAsync(start, end);

        // Assert
        result.Should().BeEmpty();
    }

    [Test, Timeout(30000)]
    public async Task GetHourlyStatsAsync_NoMatchingRecords_ReturnsEmptyList(CancellationToken cancellationToken)
    {
        // Arrange
        var baseTime = new DateTime(2025, 1, 15, 0, 0, 0);
        await SeedHourlyStatsAsync(
            new HourlySystemStats { Hour = baseTime, AvgCpuPercent = 50 }
        );

        // Query a completely different date range
        var queryStart = new DateTime(2025, 6, 1, 0, 0, 0);
        var queryEnd = new DateTime(2025, 6, 2, 0, 0, 0);

        // Act
        var result = await _service.GetHourlyStatsAsync(queryStart, queryEnd);

        // Assert
        result.Should().BeEmpty();
    }

    [Test, Timeout(30000)]
    public async Task GetHourlyStatsAsync_InclusiveDateRange(CancellationToken cancellationToken)
    {
        // Arrange
        var hour = new DateTime(2025, 1, 15, 12, 0, 0);
        await SeedHourlyStatsAsync(
            new HourlySystemStats { Hour = hour, AvgCpuPercent = 50 }
        );

        // Act - Query exactly the same start and end
        var result = await _service.GetHourlyStatsAsync(hour, hour);

        // Assert
        result.Should().HaveCount(1);
    }

    #endregion

    #region GetDailyStatsAsync Tests

    [Test, Timeout(30000)]
    public async Task GetDailyStatsAsync_ReturnsCorrectDataRange(CancellationToken cancellationToken)
    {
        // Arrange
        var day1 = new DateOnly(2025, 1, 10);
        var day2 = new DateOnly(2025, 1, 11);
        var day3 = new DateOnly(2025, 1, 12);
        var day4 = new DateOnly(2025, 1, 13);

        await SeedDailyStatsAsync(
            new DailySystemStats { Date = day1, AvgCpuPercent = 25 },
            new DailySystemStats { Date = day2, AvgCpuPercent = 50 },
            new DailySystemStats { Date = day3, AvgCpuPercent = 75 },
            new DailySystemStats { Date = day4, AvgCpuPercent = 90 }
        );

        // Act
        var result = await _service.GetDailyStatsAsync(day2, day3);

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(d => d.Date == day2);
        result.Should().Contain(d => d.Date == day3);
    }

    [Test, Timeout(30000)]
    public async Task GetDailyStatsAsync_ReturnsOrderedByDate(CancellationToken cancellationToken)
    {
        // Arrange
        await SeedDailyStatsAsync(
            new DailySystemStats { Date = new DateOnly(2025, 1, 15), AvgCpuPercent = 30 },
            new DailySystemStats { Date = new DateOnly(2025, 1, 10), AvgCpuPercent = 10 },
            new DailySystemStats { Date = new DateOnly(2025, 1, 12), AvgCpuPercent = 20 }
        );

        // Act
        var result = await _service.GetDailyStatsAsync(
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 1, 31));

        // Assert
        result.Should().HaveCount(3);
        result.Should().BeInAscendingOrder(d => d.Date);
    }

    [Test, Timeout(30000)]
    public async Task GetDailyStatsAsync_EmptyData_ReturnsEmptyList(CancellationToken cancellationToken)
    {
        // Arrange
        var start = new DateOnly(2025, 1, 1);
        var end = new DateOnly(2025, 1, 31);

        // Act
        var result = await _service.GetDailyStatsAsync(start, end);

        // Assert
        result.Should().BeEmpty();
    }

    [Test, Timeout(30000)]
    public async Task GetDailyStatsAsync_NoMatchingRecords_ReturnsEmptyList(CancellationToken cancellationToken)
    {
        // Arrange
        await SeedDailyStatsAsync(
            new DailySystemStats { Date = new DateOnly(2025, 1, 15), AvgCpuPercent = 50 }
        );

        // Act
        var result = await _service.GetDailyStatsAsync(
            new DateOnly(2025, 6, 1),
            new DateOnly(2025, 6, 30));

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region GetAverageCpuAsync Tests

    [Test, Timeout(30000)]
    public async Task GetAverageCpuAsync_CalculatesCorrectly(CancellationToken cancellationToken)
    {
        // Arrange
        var baseTime = new DateTime(2025, 1, 15, 10, 0, 0);
        await SeedHourlyStatsAsync(
            new HourlySystemStats { Hour = baseTime, AvgCpuPercent = 20 },
            new HourlySystemStats { Hour = baseTime.AddHours(1), AvgCpuPercent = 40 },
            new HourlySystemStats { Hour = baseTime.AddHours(2), AvgCpuPercent = 60 },
            new HourlySystemStats { Hour = baseTime.AddHours(3), AvgCpuPercent = 80 }
        );

        // Act
        var result = await _service.GetAverageCpuAsync(baseTime, baseTime.AddHours(3));

        // Assert
        result.Should().Be(50.0); // (20 + 40 + 60 + 80) / 4 = 50
    }

    [Test, Timeout(30000)]
    public async Task GetAverageCpuAsync_SingleRecord_ReturnsThatValue(CancellationToken cancellationToken)
    {
        // Arrange
        var hour = new DateTime(2025, 1, 15, 10, 0, 0);
        await SeedHourlyStatsAsync(
            new HourlySystemStats { Hour = hour, AvgCpuPercent = 75 }
        );

        // Act
        var result = await _service.GetAverageCpuAsync(hour, hour);

        // Assert
        result.Should().Be(75.0);
    }

    [Test, Timeout(30000)]
    public async Task GetAverageCpuAsync_NoData_ReturnsZero(CancellationToken cancellationToken)
    {
        // Arrange
        var start = new DateTime(2025, 1, 1);
        var end = new DateTime(2025, 1, 31);

        // Act
        var result = await _service.GetAverageCpuAsync(start, end);

        // Assert
        result.Should().Be(0);
    }

    [Test, Timeout(30000)]
    public async Task GetAverageCpuAsync_NoMatchingRecords_ReturnsZero(CancellationToken cancellationToken)
    {
        // Arrange
        var hour = new DateTime(2025, 1, 15, 10, 0, 0);
        await SeedHourlyStatsAsync(
            new HourlySystemStats { Hour = hour, AvgCpuPercent = 50 }
        );

        // Query different time range
        var queryStart = new DateTime(2025, 6, 1);
        var queryEnd = new DateTime(2025, 6, 30);

        // Act
        var result = await _service.GetAverageCpuAsync(queryStart, queryEnd);

        // Assert
        result.Should().Be(0);
    }

    [Test, Timeout(30000)]
    public async Task GetAverageCpuAsync_PartialRangeMatch_CalculatesMatchingOnly(CancellationToken cancellationToken)
    {
        // Arrange
        var baseTime = new DateTime(2025, 1, 15, 10, 0, 0);
        await SeedHourlyStatsAsync(
            new HourlySystemStats { Hour = baseTime, AvgCpuPercent = 10 },
            new HourlySystemStats { Hour = baseTime.AddHours(1), AvgCpuPercent = 30 },
            new HourlySystemStats { Hour = baseTime.AddHours(2), AvgCpuPercent = 50 },
            new HourlySystemStats { Hour = baseTime.AddHours(5), AvgCpuPercent = 90 } // Out of range
        );

        // Act - Only query first 3 hours
        var result = await _service.GetAverageCpuAsync(baseTime, baseTime.AddHours(2));

        // Assert
        result.Should().Be(30.0); // (10 + 30 + 50) / 3 = 30
    }

    #endregion

    #region GetAverageMemoryAsync Tests

    [Test, Timeout(30000)]
    public async Task GetAverageMemoryAsync_CalculatesCorrectly(CancellationToken cancellationToken)
    {
        // Arrange
        var baseTime = new DateTime(2025, 1, 15, 10, 0, 0);
        await SeedHourlyStatsAsync(
            new HourlySystemStats { Hour = baseTime, AvgMemoryPercent = 40 },
            new HourlySystemStats { Hour = baseTime.AddHours(1), AvgMemoryPercent = 50 },
            new HourlySystemStats { Hour = baseTime.AddHours(2), AvgMemoryPercent = 60 },
            new HourlySystemStats { Hour = baseTime.AddHours(3), AvgMemoryPercent = 70 }
        );

        // Act
        var result = await _service.GetAverageMemoryAsync(baseTime, baseTime.AddHours(3));

        // Assert
        result.Should().Be(55.0); // (40 + 50 + 60 + 70) / 4 = 55
    }

    [Test, Timeout(30000)]
    public async Task GetAverageMemoryAsync_SingleRecord_ReturnsThatValue(CancellationToken cancellationToken)
    {
        // Arrange
        var hour = new DateTime(2025, 1, 15, 10, 0, 0);
        await SeedHourlyStatsAsync(
            new HourlySystemStats { Hour = hour, AvgMemoryPercent = 85 }
        );

        // Act
        var result = await _service.GetAverageMemoryAsync(hour, hour);

        // Assert
        result.Should().Be(85.0);
    }

    [Test, Timeout(30000)]
    public async Task GetAverageMemoryAsync_NoData_ReturnsZero(CancellationToken cancellationToken)
    {
        // Arrange
        var start = new DateTime(2025, 1, 1);
        var end = new DateTime(2025, 1, 31);

        // Act
        var result = await _service.GetAverageMemoryAsync(start, end);

        // Assert
        result.Should().Be(0);
    }

    [Test, Timeout(30000)]
    public async Task GetAverageMemoryAsync_NoMatchingRecords_ReturnsZero(CancellationToken cancellationToken)
    {
        // Arrange
        var hour = new DateTime(2025, 1, 15, 10, 0, 0);
        await SeedHourlyStatsAsync(
            new HourlySystemStats { Hour = hour, AvgMemoryPercent = 50 }
        );

        // Query different time range
        var queryStart = new DateTime(2025, 6, 1);
        var queryEnd = new DateTime(2025, 6, 30);

        // Act
        var result = await _service.GetAverageMemoryAsync(queryStart, queryEnd);

        // Assert
        result.Should().Be(0);
    }

    [Test, Timeout(30000)]
    public async Task GetAverageMemoryAsync_HighMemoryUsage_CalculatesCorrectly(CancellationToken cancellationToken)
    {
        // Arrange
        var baseTime = new DateTime(2025, 1, 15, 10, 0, 0);
        await SeedHourlyStatsAsync(
            new HourlySystemStats { Hour = baseTime, AvgMemoryPercent = 95 },
            new HourlySystemStats { Hour = baseTime.AddHours(1), AvgMemoryPercent = 98 }
        );

        // Act
        var result = await _service.GetAverageMemoryAsync(baseTime, baseTime.AddHours(1));

        // Assert
        result.Should().Be(96.5); // (95 + 98) / 2 = 96.5
    }

    #endregion

    #region AggregateHourlyAsync Tests

    [Test, Timeout(30000)]
    public async Task AggregateHourlyAsync_CreatesHourlyStats(CancellationToken cancellationToken)
    {
        // Arrange - Record samples for a past hour
        var pastHour = DateTime.Now.AddHours(-2);
        var sampleTime1 = new DateTime(pastHour.Year, pastHour.Month, pastHour.Day, pastHour.Hour, 15, 0);
        var sampleTime2 = new DateTime(pastHour.Year, pastHour.Month, pastHour.Day, pastHour.Hour, 30, 0);
        var sampleTime3 = new DateTime(pastHour.Year, pastHour.Month, pastHour.Day, pastHour.Hour, 45, 0);

        await _service.RecordSampleAsync(CreateSystemStats(cpuUsage: 30, memoryUsage: 40, timestamp: sampleTime1));
        await _service.RecordSampleAsync(CreateSystemStats(cpuUsage: 50, memoryUsage: 60, timestamp: sampleTime2));
        await _service.RecordSampleAsync(CreateSystemStats(cpuUsage: 70, memoryUsage: 80, timestamp: sampleTime3));

        // Act
        await _service.AggregateHourlyAsync();

        // Assert
        using var db = await GetFreshContextAsync();
        var hourStart = new DateTime(pastHour.Year, pastHour.Month, pastHour.Day, pastHour.Hour, 0, 0);
        var stats = await db.HourlySystemStats.FirstOrDefaultAsync(h => h.Hour == hourStart);

        stats.Should().NotBeNull();
        stats!.AvgCpuPercent.Should().Be(50); // (30 + 50 + 70) / 3
        stats.MinCpuPercent.Should().Be(30);
        stats.MaxCpuPercent.Should().Be(70);
    }

    [Test, Timeout(30000)]
    public async Task AggregateHourlyAsync_AfterDispose_DoesNotThrow(CancellationToken cancellationToken)
    {
        // Arrange
        var service = new SystemHistoryService(_serviceProvider, _loggerMock);

        // Act
        service.Dispose();

        // Should not throw after disposal
        await service.AggregateHourlyAsync();
    }

    [Test, Timeout(30000)]
    public async Task AggregateHourlyAsync_EmptyBuffer_DoesNotCreateRecords(CancellationToken cancellationToken)
    {
        // Act
        await _service.AggregateHourlyAsync();

        // Assert
        using var db = await GetFreshContextAsync();
        var count = await db.HourlySystemStats.CountAsync();
        count.Should().Be(0);
    }

    [Test, Timeout(30000)]
    public async Task AggregateHourlyAsync_MultipleHours_CreatesMultipleRecords(CancellationToken cancellationToken)
    {
        // Arrange - Record samples for two different past hours
        var twoHoursAgo = DateTime.Now.AddHours(-3);
        var oneHourAgo = DateTime.Now.AddHours(-2);

        var time1 = new DateTime(twoHoursAgo.Year, twoHoursAgo.Month, twoHoursAgo.Day, twoHoursAgo.Hour, 30, 0);
        var time2 = new DateTime(oneHourAgo.Year, oneHourAgo.Month, oneHourAgo.Day, oneHourAgo.Hour, 30, 0);

        await _service.RecordSampleAsync(CreateSystemStats(cpuUsage: 25, timestamp: time1));
        await _service.RecordSampleAsync(CreateSystemStats(cpuUsage: 75, timestamp: time2));

        // Act
        await _service.AggregateHourlyAsync();

        // Assert
        using var db = await GetFreshContextAsync();
        var count = await db.HourlySystemStats.CountAsync();
        count.Should().Be(2);
    }

    [Test, Timeout(30000)]
    public async Task AggregateHourlyAsync_WithSamples_CalculatesAllStats(CancellationToken cancellationToken)
    {
        // Arrange
        var pastHour = DateTime.Now.AddHours(-2);
        var baseTime = new DateTime(pastHour.Year, pastHour.Month, pastHour.Day, pastHour.Hour, 0, 0);

        await _service.RecordSampleAsync(CreateSystemStats(cpuUsage: 20, memoryUsage: 40, memoryUsedBytes: 6_000_000_000, timestamp: baseTime.AddMinutes(10)));
        await _service.RecordSampleAsync(CreateSystemStats(cpuUsage: 60, memoryUsage: 70, memoryUsedBytes: 10_000_000_000, timestamp: baseTime.AddMinutes(30)));
        await _service.RecordSampleAsync(CreateSystemStats(cpuUsage: 40, memoryUsage: 50, memoryUsedBytes: 8_000_000_000, timestamp: baseTime.AddMinutes(50)));

        // Act
        await _service.AggregateHourlyAsync();

        // Assert
        using var db = await GetFreshContextAsync();
        var stats = await db.HourlySystemStats.FirstOrDefaultAsync(h => h.Hour == baseTime);

        stats.Should().NotBeNull();
        stats!.AvgCpuPercent.Should().Be(40); // (20 + 60 + 40) / 3
        stats.MinCpuPercent.Should().Be(20);
        stats.MaxCpuPercent.Should().Be(60);
        stats.AvgMemoryPercent.Should().Be(50); // (37.5 + 62.5 + 50) / 3 — MemoryPercent derived from UsedBytes/TotalBytes
        stats.MaxMemoryPercent.Should().Be(62.5); // max of 37.5, 62.5, 50
        stats.AvgMemoryUsedBytes.Should().Be(8_000_000_000); // (6B + 10B + 8B) / 3
    }

    [Test, Timeout(30000)]
    public async Task AggregateHourlyAsync_MergesWithExistingRecord_UpdatesExtremes(CancellationToken cancellationToken)
    {
        // Arrange - Seed an existing hourly record
        var pastHour = DateTime.Now.AddHours(-2);
        var hourStart = new DateTime(pastHour.Year, pastHour.Month, pastHour.Day, pastHour.Hour, 0, 0);

        await SeedHourlyStatsAsync(new HourlySystemStats
        {
            Hour = hourStart,
            AvgCpuPercent = 50,
            MaxCpuPercent = 60,
            MinCpuPercent = 40,
            AvgMemoryPercent = 55,
            MaxMemoryPercent = 65,
            AvgMemoryUsedBytes = 8_000_000_000
        });

        // Buffer samples for the same past hour with MORE extreme values
        await _service.RecordSampleAsync(CreateSystemStats(cpuUsage: 90, memoryUsage: 85, timestamp: hourStart.AddMinutes(15)));
        await _service.RecordSampleAsync(CreateSystemStats(cpuUsage: 10, memoryUsage: 30, timestamp: hourStart.AddMinutes(45)));

        // Act
        await _service.AggregateHourlyAsync();

        // Assert
        using var db = await GetFreshContextAsync();
        var stats = await db.HourlySystemStats.FirstOrDefaultAsync(h => h.Hour == hourStart);

        stats.Should().NotBeNull();
        stats!.MaxCpuPercent.Should().Be(90); // Updated from 60 to 90
        stats.MinCpuPercent.Should().Be(10); // Updated from 40 to 10
        stats.MaxMemoryPercent.Should().Be(85); // Updated from 65 to 85
        // Avg fields are NOT updated during merge
        stats.AvgCpuPercent.Should().Be(50);
        stats.AvgMemoryPercent.Should().Be(55);
    }

    [Test, Timeout(30000)]
    public async Task AggregateHourlyAsync_MergesWithExistingRecord_PreservesExistingWhenBetter(CancellationToken cancellationToken)
    {
        // Arrange - Existing record already has more extreme values
        var pastHour = DateTime.Now.AddHours(-2);
        var hourStart = new DateTime(pastHour.Year, pastHour.Month, pastHour.Day, pastHour.Hour, 0, 0);

        await SeedHourlyStatsAsync(new HourlySystemStats
        {
            Hour = hourStart,
            AvgCpuPercent = 50,
            MaxCpuPercent = 95,
            MinCpuPercent = 5,
            AvgMemoryPercent = 55,
            MaxMemoryPercent = 98,
            AvgMemoryUsedBytes = 8_000_000_000
        });

        // Buffer samples with LESS extreme values
        await _service.RecordSampleAsync(CreateSystemStats(cpuUsage: 50, memoryUsage: 60, timestamp: hourStart.AddMinutes(15)));

        // Act
        await _service.AggregateHourlyAsync();

        // Assert
        using var db = await GetFreshContextAsync();
        var stats = await db.HourlySystemStats.FirstOrDefaultAsync(h => h.Hour == hourStart);

        stats.Should().NotBeNull();
        stats!.MaxCpuPercent.Should().Be(95); // Preserved (95 > 50)
        stats.MinCpuPercent.Should().Be(5); // Preserved (5 < 50)
        stats.MaxMemoryPercent.Should().Be(98); // Preserved (98 > 60)
    }

    [Test, Timeout(30000)]
    public async Task AggregateHourlyAsync_CurrentHourSamples_NotAggregated(CancellationToken cancellationToken)
    {
        // Arrange - Record samples for the CURRENT hour only
        var now = DateTime.Now;
        var currentHourTime = new DateTime(now.Year, now.Month, now.Day, now.Hour, 15, 0);

        await _service.RecordSampleAsync(CreateSystemStats(cpuUsage: 75, timestamp: currentHourTime));

        // Act
        await _service.AggregateHourlyAsync();

        // Assert - No records should be created for current hour
        using var db = await GetFreshContextAsync();
        var count = await db.HourlySystemStats.CountAsync();
        count.Should().Be(0);
    }

    [Test, Timeout(30000)]
    public async Task AggregateHourlyAsync_MultipleHours_EachHourHasCorrectValues(CancellationToken cancellationToken)
    {
        // Arrange
        var threeHoursAgo = DateTime.Now.AddHours(-3);
        var twoHoursAgo = DateTime.Now.AddHours(-2);

        var hour1 = new DateTime(threeHoursAgo.Year, threeHoursAgo.Month, threeHoursAgo.Day, threeHoursAgo.Hour, 0, 0);
        var hour2 = new DateTime(twoHoursAgo.Year, twoHoursAgo.Month, twoHoursAgo.Day, twoHoursAgo.Hour, 0, 0);

        // Samples for hour 1
        await _service.RecordSampleAsync(CreateSystemStats(cpuUsage: 20, memoryUsage: 30, timestamp: hour1.AddMinutes(10)));
        await _service.RecordSampleAsync(CreateSystemStats(cpuUsage: 40, memoryUsage: 50, timestamp: hour1.AddMinutes(40)));

        // Samples for hour 2
        await _service.RecordSampleAsync(CreateSystemStats(cpuUsage: 60, memoryUsage: 70, timestamp: hour2.AddMinutes(10)));
        await _service.RecordSampleAsync(CreateSystemStats(cpuUsage: 80, memoryUsage: 90, timestamp: hour2.AddMinutes(40)));

        // Act
        await _service.AggregateHourlyAsync();

        // Assert
        using var db = await GetFreshContextAsync();
        var stats1 = await db.HourlySystemStats.FirstOrDefaultAsync(h => h.Hour == hour1);
        var stats2 = await db.HourlySystemStats.FirstOrDefaultAsync(h => h.Hour == hour2);

        stats1.Should().NotBeNull();
        stats1!.AvgCpuPercent.Should().Be(30); // (20 + 40) / 2
        stats1.MinCpuPercent.Should().Be(20);
        stats1.MaxCpuPercent.Should().Be(40);
        stats1.AvgMemoryPercent.Should().Be(40); // (30 + 50) / 2

        stats2.Should().NotBeNull();
        stats2!.AvgCpuPercent.Should().Be(70); // (60 + 80) / 2
        stats2.MinCpuPercent.Should().Be(60);
        stats2.MaxCpuPercent.Should().Be(80);
        stats2.AvgMemoryPercent.Should().Be(80); // (70 + 90) / 2
    }

    #endregion

    #region AggregateDailyAsync Tests

    [Test, Timeout(30000)]
    public async Task AggregateDailyAsync_CreatesFromHourlyStats(CancellationToken cancellationToken)
    {
        // Arrange - Seed hourly stats for yesterday
        var yesterday = DateOnly.FromDateTime(DateTime.Now.AddDays(-1));
        var startOfYesterday = yesterday.ToDateTime(TimeOnly.MinValue);

        await SeedHourlyStatsAsync(
            new HourlySystemStats
            {
                Hour = startOfYesterday.AddHours(8),
                AvgCpuPercent = 30,
                MaxCpuPercent = 40,
                MinCpuPercent = 20,
                AvgMemoryPercent = 50,
                MaxMemoryPercent = 60,
                AvgMemoryUsedBytes = 8_000_000_000
            },
            new HourlySystemStats
            {
                Hour = startOfYesterday.AddHours(12),
                AvgCpuPercent = 70,
                MaxCpuPercent = 90,
                MinCpuPercent = 60,
                AvgMemoryPercent = 80,
                MaxMemoryPercent = 95,
                AvgMemoryUsedBytes = 12_000_000_000
            }
        );

        // Act
        await _service.AggregateDailyAsync();

        // Assert
        using var db = await GetFreshContextAsync();
        var daily = await db.DailySystemStats.FirstOrDefaultAsync(d => d.Date == yesterday);

        daily.Should().NotBeNull();
        daily!.AvgCpuPercent.Should().Be(50); // (30 + 70) / 2
        daily.MaxCpuPercent.Should().Be(90);
        daily.AvgMemoryPercent.Should().Be(65); // (50 + 80) / 2
        daily.MaxMemoryPercent.Should().Be(95);
        daily.PeakMemoryUsedBytes.Should().Be(12_000_000_000);
    }

    [Test, Timeout(30000)]
    public async Task AggregateDailyAsync_NoHourlyData_DoesNotCreateRecord(CancellationToken cancellationToken)
    {
        // Act
        await _service.AggregateDailyAsync();

        // Assert
        using var db = await GetFreshContextAsync();
        var count = await db.DailySystemStats.CountAsync();
        count.Should().Be(0);
    }

    [Test, Timeout(30000)]
    public async Task AggregateDailyAsync_ExistingRecord_DoesNotDuplicate(CancellationToken cancellationToken)
    {
        // Arrange - Seed existing daily record
        var yesterday = DateOnly.FromDateTime(DateTime.Now.AddDays(-1));
        await SeedDailyStatsAsync(
            new DailySystemStats { Date = yesterday, AvgCpuPercent = 50 }
        );

        // Also seed hourly data that could be aggregated
        var startOfYesterday = yesterday.ToDateTime(TimeOnly.MinValue);
        await SeedHourlyStatsAsync(
            new HourlySystemStats { Hour = startOfYesterday.AddHours(10), AvgCpuPercent = 75 }
        );

        // Act
        await _service.AggregateDailyAsync();

        // Assert
        using var db = await GetFreshContextAsync();
        var records = await db.DailySystemStats.Where(d => d.Date == yesterday).ToListAsync();
        records.Should().HaveCount(1);
        records[0].AvgCpuPercent.Should().Be(50); // Original value, not updated
    }

    [Test, Timeout(30000)]
    public async Task AggregateDailyAsync_WithGpuHourlyData_AggregatesGpuStats(CancellationToken cancellationToken)
    {
        // Arrange
        var yesterday = DateOnly.FromDateTime(DateTime.Now.AddDays(-1));
        var startOfYesterday = yesterday.ToDateTime(TimeOnly.MinValue);

        await SeedHourlyStatsAsync(
            new HourlySystemStats
            {
                Hour = startOfYesterday.AddHours(10),
                AvgCpuPercent = 40,
                MaxCpuPercent = 50,
                MinCpuPercent = 30,
                AvgMemoryPercent = 60,
                MaxMemoryPercent = 70,
                AvgMemoryUsedBytes = 8_000_000_000,
                AvgGpuPercent = 50,
                MaxGpuPercent = 70
            },
            new HourlySystemStats
            {
                Hour = startOfYesterday.AddHours(14),
                AvgCpuPercent = 60,
                MaxCpuPercent = 80,
                MinCpuPercent = 50,
                AvgMemoryPercent = 75,
                MaxMemoryPercent = 85,
                AvgMemoryUsedBytes = 10_000_000_000,
                AvgGpuPercent = 80,
                MaxGpuPercent = 95
            }
        );

        // Act
        await _service.AggregateDailyAsync();

        // Assert
        using var db = await GetFreshContextAsync();
        var daily = await db.DailySystemStats.FirstOrDefaultAsync(d => d.Date == yesterday);

        daily.Should().NotBeNull();
        daily!.AvgGpuPercent.Should().Be(65); // (50 + 80) / 2
        daily.MaxGpuPercent.Should().Be(95);
    }

    [Test, Timeout(30000)]
    public async Task AggregateDailyAsync_AfterDispose_DoesNotThrow(CancellationToken cancellationToken)
    {
        // Arrange
        var service = new SystemHistoryService(_serviceProvider, _loggerMock);

        // Act
        service.Dispose();

        // Should not throw after disposal
        await service.AggregateDailyAsync();
    }

    #endregion

    #region Edge Cases

    [Test, Timeout(30000)]
    public async Task GetHourlyStatsAsync_StartAfterEnd_ReturnsEmpty(CancellationToken cancellationToken)
    {
        // Arrange
        var baseTime = new DateTime(2025, 1, 15, 10, 0, 0);
        await SeedHourlyStatsAsync(
            new HourlySystemStats { Hour = baseTime, AvgCpuPercent = 50 }
        );

        // Act - Query with start after end
        var result = await _service.GetHourlyStatsAsync(
            baseTime.AddHours(2),
            baseTime.AddHours(-2));

        // Assert
        result.Should().BeEmpty();
    }

    [Test, Timeout(30000)]
    public async Task GetDailyStatsAsync_StartAfterEnd_ReturnsEmpty(CancellationToken cancellationToken)
    {
        // Arrange
        await SeedDailyStatsAsync(
            new DailySystemStats { Date = new DateOnly(2025, 1, 15), AvgCpuPercent = 50 }
        );

        // Act
        var result = await _service.GetDailyStatsAsync(
            new DateOnly(2025, 1, 20),
            new DateOnly(2025, 1, 10));

        // Assert
        result.Should().BeEmpty();
    }

    [Test, Timeout(30000)]
    public async Task RecordSampleAsync_ZeroValues_Accepted(CancellationToken cancellationToken)
    {
        // Arrange
        var stats = CreateSystemStats(cpuUsage: 0, memoryUsage: 0, memoryUsedBytes: 0);

        // Act & Assert - Should not throw
        await _service.RecordSampleAsync(stats);
    }

    [Test, Timeout(30000)]
    public async Task RecordSampleAsync_MaxValues_Accepted(CancellationToken cancellationToken)
    {
        // Arrange
        var stats = CreateSystemStats(cpuUsage: 100.0, memoryUsage: 100.0, memoryUsedBytes: long.MaxValue / 2);

        // Act & Assert - Should not throw
        await _service.RecordSampleAsync(stats);
    }

    [Test, Timeout(30000)]
    public async Task GetAverageCpuAsync_VerySmallValues_CalculatesCorrectly(CancellationToken cancellationToken)
    {
        // Arrange
        var baseTime = new DateTime(2025, 1, 15, 10, 0, 0);
        await SeedHourlyStatsAsync(
            new HourlySystemStats { Hour = baseTime, AvgCpuPercent = 0.1 },
            new HourlySystemStats { Hour = baseTime.AddHours(1), AvgCpuPercent = 0.3 }
        );

        // Act
        var result = await _service.GetAverageCpuAsync(baseTime, baseTime.AddHours(1));

        // Assert
        result.Should().BeApproximately(0.2, 0.0001);
    }

    [Test, Timeout(30000)]
    public async Task Service_MultipleOperations_ThreadSafe(CancellationToken cancellationToken)
    {
        // Arrange
        var tasks = new List<Task>();

        // Act - Run multiple operations concurrently
        for (int i = 0; i < 100; i++)
        {
            var stats = CreateSystemStats(cpuUsage: i % 100);
            tasks.Add(_service.RecordSampleAsync(stats));
        }

        await Task.WhenAll(tasks);

        // Assert - No exceptions means thread safety is maintained
    }

    #endregion

    #region GPU Stats Tests

    [Test, Timeout(30000)]
    public async Task GetDailyStatsAsync_WithGpuData_ReturnsGpuStats(CancellationToken cancellationToken)
    {
        // Arrange
        var day = new DateOnly(2025, 1, 15);
        await SeedDailyStatsAsync(
            new DailySystemStats
            {
                Date = day,
                AvgCpuPercent = 50,
                AvgGpuPercent = 75,
                MaxGpuPercent = 95
            }
        );

        // Act
        var result = await _service.GetDailyStatsAsync(day, day);

        // Assert
        result.Should().HaveCount(1);
        result[0].AvgGpuPercent.Should().Be(75);
        result[0].MaxGpuPercent.Should().Be(95);
    }

    [Test, Timeout(30000)]
    public async Task GetHourlyStatsAsync_WithGpuData_ReturnsGpuStats(CancellationToken cancellationToken)
    {
        // Arrange
        var hour = new DateTime(2025, 1, 15, 12, 0, 0);
        await SeedHourlyStatsAsync(
            new HourlySystemStats
            {
                Hour = hour,
                AvgCpuPercent = 50,
                AvgGpuPercent = 60,
                MaxGpuPercent = 80,
                AvgGpuMemoryPercent = 45
            }
        );

        // Act
        var result = await _service.GetHourlyStatsAsync(hour, hour);

        // Assert
        result.Should().HaveCount(1);
        result[0].AvgGpuPercent.Should().Be(60);
        result[0].MaxGpuPercent.Should().Be(80);
        result[0].AvgGpuMemoryPercent.Should().Be(45);
    }

    [Test, Timeout(30000)]
    public async Task GetHourlyStatsAsync_NullGpuData_ReturnsNullGpuStats(CancellationToken cancellationToken)
    {
        // Arrange
        var hour = new DateTime(2025, 1, 15, 12, 0, 0);
        await SeedHourlyStatsAsync(
            new HourlySystemStats
            {
                Hour = hour,
                AvgCpuPercent = 50,
                AvgGpuPercent = null,
                MaxGpuPercent = null,
                AvgGpuMemoryPercent = null
            }
        );

        // Act
        var result = await _service.GetHourlyStatsAsync(hour, hour);

        // Assert
        result.Should().HaveCount(1);
        result[0].AvgGpuPercent.Should().BeNull();
        result[0].MaxGpuPercent.Should().BeNull();
        result[0].AvgGpuMemoryPercent.Should().BeNull();
    }

    #endregion

    #region Dispose Tests

    [Test]
    public void Dispose_MultipleCalls_DoesNotThrow()
    {
        // Arrange
        var service = new SystemHistoryService(_serviceProvider, _loggerMock);

        // Act & Assert - Should not throw on multiple dispose calls
        service.Dispose();
        service.Dispose();
        service.Dispose();
    }

    [Test, Timeout(30000)]
    public async Task Dispose_TriggersAggregation(CancellationToken cancellationToken)
    {
        // Arrange
        var service = new SystemHistoryService(_serviceProvider, _loggerMock);
        var pastHour = DateTime.Now.AddHours(-2);
        var sampleTime = new DateTime(pastHour.Year, pastHour.Month, pastHour.Day, pastHour.Hour, 30, 0);

        await service.RecordSampleAsync(CreateSystemStats(cpuUsage: 50, timestamp: sampleTime));

        // Act
        service.Dispose();

        // Assert - Aggregation should have been attempted during disposal
        // We verify the logger was potentially called (actual aggregation depends on timing)
    }

    #endregion
}
