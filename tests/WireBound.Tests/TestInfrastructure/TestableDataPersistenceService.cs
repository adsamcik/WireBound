using Microsoft.EntityFrameworkCore;
using WireBound.Models;
using WireBound.Services;
using WireBound.Tests.TestInfrastructure;

namespace WireBound.Tests.Services;

/// <summary>
/// A testable version of DataPersistenceService that uses TestableDbContext
/// </summary>
public class TestableDataPersistenceService : IDataPersistenceService
{
    private readonly TestableDbContext _db;
    private long _lastSavedReceived = 0;
    private long _lastSavedSent = 0;

    public TestableDataPersistenceService(TestableDbContext db)
    {
        _db = db;
    }

    public async Task SaveStatsAsync(NetworkStats stats)
    {
        var now = DateTime.Now;
        var currentHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);
        var today = DateOnly.FromDateTime(now);

        // Calculate delta since last save
        var receivedDelta = stats.SessionBytesReceived - _lastSavedReceived;
        var sentDelta = stats.SessionBytesSent - _lastSavedSent;

        if (receivedDelta < 0) receivedDelta = stats.SessionBytesReceived;
        if (sentDelta < 0) sentDelta = stats.SessionBytesSent;

        _lastSavedReceived = stats.SessionBytesReceived;
        _lastSavedSent = stats.SessionBytesSent;

        // Update or create hourly record
        var hourlyRecord = await _db.HourlyUsages
            .FirstOrDefaultAsync(h => h.Hour == currentHour && h.AdapterId == stats.AdapterId);

        if (hourlyRecord == null)
        {
            hourlyRecord = new HourlyUsage
            {
                Hour = currentHour,
                AdapterId = stats.AdapterId,
                BytesReceived = receivedDelta,
                BytesSent = sentDelta,
                PeakDownloadSpeed = stats.DownloadSpeedBps,
                PeakUploadSpeed = stats.UploadSpeedBps,
                LastUpdated = now
            };
            _db.HourlyUsages.Add(hourlyRecord);
        }
        else
        {
            hourlyRecord.BytesReceived += receivedDelta;
            hourlyRecord.BytesSent += sentDelta;
            hourlyRecord.PeakDownloadSpeed = Math.Max(hourlyRecord.PeakDownloadSpeed, stats.DownloadSpeedBps);
            hourlyRecord.PeakUploadSpeed = Math.Max(hourlyRecord.PeakUploadSpeed, stats.UploadSpeedBps);
            hourlyRecord.LastUpdated = now;
        }

        // Update or create daily record
        var dailyRecord = await _db.DailyUsages
            .FirstOrDefaultAsync(d => d.Date == today && d.AdapterId == stats.AdapterId);

        if (dailyRecord == null)
        {
            dailyRecord = new DailyUsage
            {
                Date = today,
                AdapterId = stats.AdapterId,
                BytesReceived = receivedDelta,
                BytesSent = sentDelta,
                PeakDownloadSpeed = stats.DownloadSpeedBps,
                PeakUploadSpeed = stats.UploadSpeedBps,
                LastUpdated = now
            };
            _db.DailyUsages.Add(dailyRecord);
        }
        else
        {
            dailyRecord.BytesReceived += receivedDelta;
            dailyRecord.BytesSent += sentDelta;
            dailyRecord.PeakDownloadSpeed = Math.Max(dailyRecord.PeakDownloadSpeed, stats.DownloadSpeedBps);
            dailyRecord.PeakUploadSpeed = Math.Max(dailyRecord.PeakUploadSpeed, stats.UploadSpeedBps);
            dailyRecord.LastUpdated = now;
        }

        await _db.SaveChangesAsync();
    }

    public async Task<List<DailyUsage>> GetDailyUsageAsync(DateOnly startDate, DateOnly endDate)
    {
        return await _db.DailyUsages
            .Where(d => d.Date >= startDate && d.Date <= endDate)
            .OrderBy(d => d.Date)
            .ToListAsync();
    }

    public async Task<List<HourlyUsage>> GetHourlyUsageAsync(DateOnly date)
    {
        var startOfDay = date.ToDateTime(TimeOnly.MinValue);
        var endOfDay = date.ToDateTime(TimeOnly.MaxValue);

        return await _db.HourlyUsages
            .Where(h => h.Hour >= startOfDay && h.Hour <= endOfDay)
            .OrderBy(h => h.Hour)
            .ToListAsync();
    }

    public async Task<List<HourlyUsage>> GetHourlyUsageRangeAsync(DateOnly startDate, DateOnly endDate)
    {
        var startDateTime = startDate.ToDateTime(TimeOnly.MinValue);
        var endDateTime = endDate.ToDateTime(TimeOnly.MaxValue);

        return await _db.HourlyUsages
            .Where(h => h.Hour >= startDateTime && h.Hour <= endDateTime)
            .OrderBy(h => h.Hour)
            .ToListAsync();
    }

    public async Task<List<WeeklyUsage>> GetWeeklyUsageAsync(DateOnly startDate, DateOnly endDate)
    {
        return await _db.WeeklyUsages
            .Where(w => w.WeekStart >= startDate && w.WeekStart <= endDate)
            .OrderBy(w => w.WeekStart)
            .ToListAsync();
    }

    public async Task<(long totalReceived, long totalSent)> GetTotalUsageAsync()
    {
        var totals = await _db.DailyUsages
            .GroupBy(_ => 1)
            .Select(g => new
            {
                TotalReceived = g.Sum(d => d.BytesReceived),
                TotalSent = g.Sum(d => d.BytesSent)
            })
            .FirstOrDefaultAsync();

        return (totals?.TotalReceived ?? 0, totals?.TotalSent ?? 0);
    }

    public async Task<(long received, long sent)> GetTodayUsageAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var usage = await _db.DailyUsages
            .Where(d => d.Date == today)
            .FirstOrDefaultAsync();

        return (usage?.BytesReceived ?? 0, usage?.BytesSent ?? 0);
    }

    public async Task<(long received, long sent)> GetThisWeekUsageAsync()
    {
        var today = DateTime.Today;
        var daysSinceMonday = ((int)today.DayOfWeek - 1 + 7) % 7;
        var monday = DateOnly.FromDateTime(today.AddDays(-daysSinceMonday));
        var todayDate = DateOnly.FromDateTime(today);

        var totals = await _db.DailyUsages
            .Where(d => d.Date >= monday && d.Date <= todayDate)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                TotalReceived = g.Sum(d => d.BytesReceived),
                TotalSent = g.Sum(d => d.BytesSent)
            })
            .FirstOrDefaultAsync();

        return (totals?.TotalReceived ?? 0, totals?.TotalSent ?? 0);
    }

    public async Task<(long received, long sent)> GetThisMonthUsageAsync()
    {
        var today = DateTime.Today;
        var firstOfMonth = new DateOnly(today.Year, today.Month, 1);
        var todayDate = DateOnly.FromDateTime(today);

        var totals = await _db.DailyUsages
            .Where(d => d.Date >= firstOfMonth && d.Date <= todayDate)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                TotalReceived = g.Sum(d => d.BytesReceived),
                TotalSent = g.Sum(d => d.BytesSent)
            })
            .FirstOrDefaultAsync();

        return (totals?.TotalReceived ?? 0, totals?.TotalSent ?? 0);
    }

    public async Task CleanupOldDataAsync(int retentionDays)
    {
        var cutoffDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-retentionDays));
        var cutoffDateTime = cutoffDate.ToDateTime(TimeOnly.MinValue);

        // In-memory provider doesn't support ExecuteDeleteAsync, use traditional delete
        var oldDailyRecords = await _db.DailyUsages.Where(d => d.Date < cutoffDate).ToListAsync();
        _db.DailyUsages.RemoveRange(oldDailyRecords);
        
        var oldHourlyRecords = await _db.HourlyUsages.Where(h => h.Hour < cutoffDateTime).ToListAsync();
        _db.HourlyUsages.RemoveRange(oldHourlyRecords);
        
        await _db.SaveChangesAsync();
    }

    public async Task<AppSettings> GetSettingsAsync()
    {
        var settings = await _db.Settings.FirstOrDefaultAsync();
        return settings ?? new AppSettings();
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        var existing = await _db.Settings.FirstOrDefaultAsync();
        if (existing == null)
        {
            settings.Id = 1;
            _db.Settings.Add(settings);
        }
        else
        {
            _db.Entry(existing).CurrentValues.SetValues(settings);
        }

        await _db.SaveChangesAsync();
    }

    // === Per-App Network Tracking Methods (stubs for testing) ===

    public Task SaveAppStatsAsync(IEnumerable<ProcessNetworkStats> stats)
    {
        // Stub implementation for testing
        return Task.CompletedTask;
    }

    public Task<List<AppUsageRecord>> GetTopAppsAsync(int count, DateOnly startDate, DateOnly endDate)
    {
        return Task.FromResult(new List<AppUsageRecord>());
    }

    public Task<List<AppUsageRecord>> GetAppUsageAsync(string appIdentifier, DateOnly startDate, DateOnly endDate, UsageGranularity? granularity = null)
    {
        return Task.FromResult(new List<AppUsageRecord>());
    }

    public Task<List<AppUsageRecord>> GetAllAppUsageAsync(DateOnly startDate, DateOnly endDate, UsageGranularity? granularity = null)
    {
        return Task.FromResult(new List<AppUsageRecord>());
    }

    public Task AggregateAppDataAsync(int aggregateAfterDays)
    {
        return Task.CompletedTask;
    }

    public Task CleanupOldAppDataAsync(int retentionDays)
    {
        return Task.CompletedTask;
    }
}
