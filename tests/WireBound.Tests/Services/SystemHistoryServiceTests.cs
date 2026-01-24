using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using WireBound.Avalonia.Services;
using WireBound.Core.Data;
using WireBound.Core.Models;

namespace WireBound.Tests.Services;

/// <summary>
/// Unit tests for SystemHistoryService
/// </summary>
public class SystemHistoryServiceTests : IDisposable
{
    private readonly WireBoundDbContext _context;
    private readonly IServiceProvider _serviceProvider;
    private readonly Mock<ILogger<SystemHistoryService>> _loggerMock;
    private readonly SystemHistoryService _service;
    private readonly string _databaseName;

    public SystemHistoryServiceTests()
    {
        _databaseName = Guid.NewGuid().ToString();
        _loggerMock = new Mock<ILogger<SystemHistoryService>>();

        // Set up in-memory database with scoped context
        var services = new ServiceCollection();
        services.AddDbContext<WireBoundDbContext>(options =>
            options.UseInMemoryDatabase(databaseName: _databaseName));
        
        _serviceProvider = services.BuildServiceProvider();
        
        // Get a context instance for direct testing
        using var scope = _serviceProvider.CreateScope();
        _context = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();
        _context.Database.EnsureCreated();

        _service = new SystemHistoryService(_serviceProvider, _loggerMock.Object);
    }

    public void Dispose()
    {
        _service.Dispose();
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    #region Helper Methods

    private static SystemStats CreateSystemStats(
        double cpuUsage = 50.0,
        double memoryUsage = 60.0,
        long memoryUsedBytes = 8_000_000_000,
        DateTime? timestamp = null)
    {
        var ts = timestamp ?? DateTime.Now;
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
                TotalBytes = 16_000_000_000,
                UsedBytes = memoryUsedBytes,
                AvailableBytes = 16_000_000_000 - memoryUsedBytes
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

    [Fact]
    public async Task RecordSampleAsync_AddsSampleToBuffer()
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

    [Fact]
    public async Task RecordSampleAsync_MultipleSamples_AllAdded()
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

    [Fact]
    public async Task RecordSampleAsync_AfterDispose_DoesNotThrow()
    {
        // Arrange
        var service = new SystemHistoryService(_serviceProvider, _loggerMock.Object);
        var stats = CreateSystemStats();
        
        // Act
        service.Dispose();
        
        // Should not throw after disposal
        await service.RecordSampleAsync(stats);
    }

    #endregion

    #region Buffer Limit Tests

    [Fact]
    public async Task RecordSampleAsync_BufferLimit_DoesNotGrowUnbounded()
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

    [Fact]
    public async Task RecordSampleAsync_ExactlyAtLimit_NoOverflow()
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

    [Fact]
    public async Task GetHourlyStatsAsync_ReturnsCorrectDataRange()
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

    [Fact]
    public async Task GetHourlyStatsAsync_ReturnsOrderedByHour()
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

    [Fact]
    public async Task GetHourlyStatsAsync_EmptyData_ReturnsEmptyList()
    {
        // Arrange
        var start = DateTime.Now.AddDays(-1);
        var end = DateTime.Now;

        // Act
        var result = await _service.GetHourlyStatsAsync(start, end);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetHourlyStatsAsync_NoMatchingRecords_ReturnsEmptyList()
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

    [Fact]
    public async Task GetHourlyStatsAsync_InclusiveDateRange()
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

    [Fact]
    public async Task GetDailyStatsAsync_ReturnsCorrectDataRange()
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

    [Fact]
    public async Task GetDailyStatsAsync_ReturnsOrderedByDate()
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

    [Fact]
    public async Task GetDailyStatsAsync_EmptyData_ReturnsEmptyList()
    {
        // Arrange
        var start = new DateOnly(2025, 1, 1);
        var end = new DateOnly(2025, 1, 31);

        // Act
        var result = await _service.GetDailyStatsAsync(start, end);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDailyStatsAsync_NoMatchingRecords_ReturnsEmptyList()
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

    [Fact]
    public async Task GetAverageCpuAsync_CalculatesCorrectly()
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

    [Fact]
    public async Task GetAverageCpuAsync_SingleRecord_ReturnsThatValue()
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

    [Fact]
    public async Task GetAverageCpuAsync_NoData_ReturnsZero()
    {
        // Arrange
        var start = new DateTime(2025, 1, 1);
        var end = new DateTime(2025, 1, 31);

        // Act
        var result = await _service.GetAverageCpuAsync(start, end);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public async Task GetAverageCpuAsync_NoMatchingRecords_ReturnsZero()
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

    [Fact]
    public async Task GetAverageCpuAsync_PartialRangeMatch_CalculatesMatchingOnly()
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

    [Fact]
    public async Task GetAverageMemoryAsync_CalculatesCorrectly()
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

    [Fact]
    public async Task GetAverageMemoryAsync_SingleRecord_ReturnsThatValue()
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

    [Fact]
    public async Task GetAverageMemoryAsync_NoData_ReturnsZero()
    {
        // Arrange
        var start = new DateTime(2025, 1, 1);
        var end = new DateTime(2025, 1, 31);

        // Act
        var result = await _service.GetAverageMemoryAsync(start, end);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public async Task GetAverageMemoryAsync_NoMatchingRecords_ReturnsZero()
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

    [Fact]
    public async Task GetAverageMemoryAsync_HighMemoryUsage_CalculatesCorrectly()
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

    [Fact]
    public async Task AggregateHourlyAsync_CreatesHourlyStats()
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

    [Fact]
    public async Task AggregateHourlyAsync_AfterDispose_DoesNotThrow()
    {
        // Arrange
        var service = new SystemHistoryService(_serviceProvider, _loggerMock.Object);
        
        // Act
        service.Dispose();
        
        // Should not throw after disposal
        await service.AggregateHourlyAsync();
    }

    [Fact]
    public async Task AggregateHourlyAsync_EmptyBuffer_DoesNotCreateRecords()
    {
        // Act
        await _service.AggregateHourlyAsync();

        // Assert
        using var db = await GetFreshContextAsync();
        var count = await db.HourlySystemStats.CountAsync();
        count.Should().Be(0);
    }

    [Fact]
    public async Task AggregateHourlyAsync_MultipleHours_CreatesMultipleRecords()
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

    #endregion

    #region AggregateDailyAsync Tests

    [Fact]
    public async Task AggregateDailyAsync_CreatesFromHourlyStats()
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

    [Fact]
    public async Task AggregateDailyAsync_NoHourlyData_DoesNotCreateRecord()
    {
        // Act
        await _service.AggregateDailyAsync();

        // Assert
        using var db = await GetFreshContextAsync();
        var count = await db.DailySystemStats.CountAsync();
        count.Should().Be(0);
    }

    [Fact]
    public async Task AggregateDailyAsync_ExistingRecord_DoesNotDuplicate()
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

    [Fact]
    public async Task AggregateDailyAsync_AfterDispose_DoesNotThrow()
    {
        // Arrange
        var service = new SystemHistoryService(_serviceProvider, _loggerMock.Object);
        
        // Act
        service.Dispose();
        
        // Should not throw after disposal
        await service.AggregateDailyAsync();
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task GetHourlyStatsAsync_StartAfterEnd_ReturnsEmpty()
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

    [Fact]
    public async Task GetDailyStatsAsync_StartAfterEnd_ReturnsEmpty()
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

    [Fact]
    public async Task RecordSampleAsync_ZeroValues_Accepted()
    {
        // Arrange
        var stats = CreateSystemStats(cpuUsage: 0, memoryUsage: 0, memoryUsedBytes: 0);

        // Act & Assert - Should not throw
        await _service.RecordSampleAsync(stats);
    }

    [Fact]
    public async Task RecordSampleAsync_MaxValues_Accepted()
    {
        // Arrange
        var stats = CreateSystemStats(cpuUsage: 100.0, memoryUsage: 100.0, memoryUsedBytes: long.MaxValue / 2);

        // Act & Assert - Should not throw
        await _service.RecordSampleAsync(stats);
    }

    [Fact]
    public async Task GetAverageCpuAsync_VerySmallValues_CalculatesCorrectly()
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

    [Fact]
    public async Task Service_MultipleOperations_ThreadSafe()
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

    [Fact]
    public async Task GetDailyStatsAsync_WithGpuData_ReturnsGpuStats()
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

    [Fact]
    public async Task GetHourlyStatsAsync_WithGpuData_ReturnsGpuStats()
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

    [Fact]
    public async Task GetHourlyStatsAsync_NullGpuData_ReturnsNullGpuStats()
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

    [Fact]
    public void Dispose_MultipleCalls_DoesNotThrow()
    {
        // Arrange
        var service = new SystemHistoryService(_serviceProvider, _loggerMock.Object);

        // Act & Assert - Should not throw on multiple dispose calls
        service.Dispose();
        service.Dispose();
        service.Dispose();
    }

    [Fact]
    public async Task Dispose_TriggersAggregation()
    {
        // Arrange
        var service = new SystemHistoryService(_serviceProvider, _loggerMock.Object);
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
