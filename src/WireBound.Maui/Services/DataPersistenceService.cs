using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WireBound.Maui.Data;
using WireBound.Maui.Models;

namespace WireBound.Maui.Services;

public class DataPersistenceService : IDataPersistenceService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly object _statsLock = new();
    private long _lastSavedReceived = 0;
    private long _lastSavedSent = 0;

    public DataPersistenceService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task SaveStatsAsync(NetworkStats stats)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var now = DateTime.Now;
        var currentHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);
        var today = DateOnly.FromDateTime(now);

        // Calculate delta since last save (thread-safe access)
        long receivedDelta;
        long sentDelta;
        lock (_statsLock)
        {
            receivedDelta = stats.SessionBytesReceived - _lastSavedReceived;
            sentDelta = stats.SessionBytesSent - _lastSavedSent;

            if (receivedDelta < 0) receivedDelta = stats.SessionBytesReceived;
            if (sentDelta < 0) sentDelta = stats.SessionBytesSent;

            _lastSavedReceived = stats.SessionBytesReceived;
            _lastSavedSent = stats.SessionBytesSent;
        }

        // Update or create hourly record
        var hourlyRecord = await db.HourlyUsages
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
            db.HourlyUsages.Add(hourlyRecord);
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
        var dailyRecord = await db.DailyUsages
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
            db.DailyUsages.Add(dailyRecord);
        }
        else
        {
            dailyRecord.BytesReceived += receivedDelta;
            dailyRecord.BytesSent += sentDelta;
            dailyRecord.PeakDownloadSpeed = Math.Max(dailyRecord.PeakDownloadSpeed, stats.DownloadSpeedBps);
            dailyRecord.PeakUploadSpeed = Math.Max(dailyRecord.PeakUploadSpeed, stats.UploadSpeedBps);
            dailyRecord.LastUpdated = now;
        }

        await db.SaveChangesAsync();
    }

    public async Task<List<DailyUsage>> GetDailyUsageAsync(DateOnly startDate, DateOnly endDate)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        return await db.DailyUsages
            .Where(d => d.Date >= startDate && d.Date <= endDate)
            .OrderBy(d => d.Date)
            .ToListAsync();
    }

    public async Task<List<HourlyUsage>> GetHourlyUsageAsync(DateOnly date)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var startOfDay = date.ToDateTime(TimeOnly.MinValue);
        var endOfDay = date.ToDateTime(TimeOnly.MaxValue);

        return await db.HourlyUsages
            .Where(h => h.Hour >= startOfDay && h.Hour <= endOfDay)
            .OrderBy(h => h.Hour)
            .ToListAsync();
    }

    public async Task<(long totalReceived, long totalSent)> GetTotalUsageAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var totals = await db.DailyUsages
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
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var cutoffDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-retentionDays));
        var cutoffDateTime = cutoffDate.ToDateTime(TimeOnly.MinValue);

        await db.DailyUsages.Where(d => d.Date < cutoffDate).ExecuteDeleteAsync();
        await db.HourlyUsages.Where(h => h.Hour < cutoffDateTime).ExecuteDeleteAsync();
    }

    public async Task<AppSettings> GetSettingsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var settings = await db.Settings.FirstOrDefaultAsync();
        return settings ?? new AppSettings();
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var existing = await db.Settings.FirstOrDefaultAsync();
        if (existing == null)
        {
            settings.Id = 1;
            db.Settings.Add(settings);
        }
        else
        {
            db.Entry(existing).CurrentValues.SetValues(settings);
        }

        await db.SaveChangesAsync();
    }

    // === Per-App Network Tracking Methods ===

    public async Task SaveAppStatsAsync(IEnumerable<ProcessNetworkStats> stats)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var now = DateTime.Now;
        var currentHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);

        foreach (var stat in stats)
        {
            if (string.IsNullOrEmpty(stat.AppIdentifier))
                continue;

            // Find or create hourly record for this app
            var record = await db.AppUsageRecords
                .FirstOrDefaultAsync(a => 
                    a.Timestamp == currentHour && 
                    a.AppIdentifier == stat.AppIdentifier && 
                    a.Granularity == UsageGranularity.Hourly);

            if (record == null)
            {
                record = new AppUsageRecord
                {
                    AppIdentifier = stat.AppIdentifier,
                    AppName = stat.DisplayName,
                    ExecutablePath = stat.ExecutablePath,
                    ProcessName = stat.ProcessName,
                    Timestamp = currentHour,
                    Granularity = UsageGranularity.Hourly,
                    BytesReceived = stat.SessionBytesReceived,
                    BytesSent = stat.SessionBytesSent,
                    PeakDownloadSpeed = stat.DownloadSpeedBps,
                    PeakUploadSpeed = stat.UploadSpeedBps,
                    LastUpdated = now
                };
                db.AppUsageRecords.Add(record);
            }
            else
            {
                record.BytesReceived = Math.Max(record.BytesReceived, stat.SessionBytesReceived);
                record.BytesSent = Math.Max(record.BytesSent, stat.SessionBytesSent);
                record.PeakDownloadSpeed = Math.Max(record.PeakDownloadSpeed, stat.DownloadSpeedBps);
                record.PeakUploadSpeed = Math.Max(record.PeakUploadSpeed, stat.UploadSpeedBps);
                record.LastUpdated = now;
            }
        }

        await db.SaveChangesAsync();
    }

    public async Task<List<AppUsageRecord>> GetTopAppsAsync(int count, DateOnly startDate, DateOnly endDate)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var startDateTime = startDate.ToDateTime(TimeOnly.MinValue);
        var endDateTime = endDate.ToDateTime(TimeOnly.MaxValue);

        var topApps = await db.AppUsageRecords
            .Where(a => a.Timestamp >= startDateTime && a.Timestamp <= endDateTime)
            .GroupBy(a => a.AppIdentifier)
            .Select(g => new
            {
                AppIdentifier = g.Key,
                AppName = g.First().AppName,
                ExecutablePath = g.First().ExecutablePath,
                ProcessName = g.First().ProcessName,
                TotalBytesReceived = g.Sum(a => a.BytesReceived),
                TotalBytesSent = g.Sum(a => a.BytesSent),
                PeakDownloadSpeed = g.Max(a => a.PeakDownloadSpeed),
                PeakUploadSpeed = g.Max(a => a.PeakUploadSpeed)
            })
            .OrderByDescending(a => a.TotalBytesReceived + a.TotalBytesSent)
            .Take(count)
            .ToListAsync();

        return topApps.Select(a => new AppUsageRecord
        {
            AppIdentifier = a.AppIdentifier,
            AppName = a.AppName,
            ExecutablePath = a.ExecutablePath,
            ProcessName = a.ProcessName,
            BytesReceived = a.TotalBytesReceived,
            BytesSent = a.TotalBytesSent,
            PeakDownloadSpeed = a.PeakDownloadSpeed,
            PeakUploadSpeed = a.PeakUploadSpeed,
            Timestamp = DateTime.Now,
            Granularity = UsageGranularity.Daily
        }).ToList();
    }

    public async Task<List<AppUsageRecord>> GetAppUsageAsync(string appIdentifier, DateOnly startDate, DateOnly endDate, UsageGranularity? granularity = null)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var startDateTime = startDate.ToDateTime(TimeOnly.MinValue);
        var endDateTime = endDate.ToDateTime(TimeOnly.MaxValue);

        var query = db.AppUsageRecords
            .Where(a => a.AppIdentifier == appIdentifier && 
                        a.Timestamp >= startDateTime && 
                        a.Timestamp <= endDateTime);

        if (granularity.HasValue)
        {
            query = query.Where(a => a.Granularity == granularity.Value);
        }

        return await query
            .OrderBy(a => a.Timestamp)
            .ToListAsync();
    }

    public async Task<List<AppUsageRecord>> GetAllAppUsageAsync(DateOnly startDate, DateOnly endDate, UsageGranularity? granularity = null)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var startDateTime = startDate.ToDateTime(TimeOnly.MinValue);
        var endDateTime = endDate.ToDateTime(TimeOnly.MaxValue);

        var query = db.AppUsageRecords
            .Where(a => a.Timestamp >= startDateTime && a.Timestamp <= endDateTime);

        if (granularity.HasValue)
        {
            query = query.Where(a => a.Granularity == granularity.Value);
        }

        return await query
            .OrderByDescending(a => a.BytesReceived + a.BytesSent)
            .ToListAsync();
    }

    public async Task AggregateAppDataAsync(int aggregateAfterDays)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var cutoffDate = DateTime.Now.AddDays(-aggregateAfterDays);
        
        var hourlyRecords = await db.AppUsageRecords
            .Where(a => a.Granularity == UsageGranularity.Hourly && a.Timestamp < cutoffDate)
            .ToListAsync();

        if (!hourlyRecords.Any())
            return;

        var groupedByDay = hourlyRecords
            .GroupBy(a => new { a.AppIdentifier, Date = DateOnly.FromDateTime(a.Timestamp) });

        foreach (var group in groupedByDay)
        {
            var dailyTimestamp = group.Key.Date.ToDateTime(TimeOnly.MinValue);
            
            var existingDaily = await db.AppUsageRecords
                .FirstOrDefaultAsync(a => 
                    a.AppIdentifier == group.Key.AppIdentifier && 
                    a.Timestamp == dailyTimestamp && 
                    a.Granularity == UsageGranularity.Daily);

            var first = group.First();
            var totalReceived = group.Sum(a => a.BytesReceived);
            var totalSent = group.Sum(a => a.BytesSent);
            var peakDown = group.Max(a => a.PeakDownloadSpeed);
            var peakUp = group.Max(a => a.PeakUploadSpeed);

            if (existingDaily == null)
            {
                db.AppUsageRecords.Add(new AppUsageRecord
                {
                    AppIdentifier = group.Key.AppIdentifier,
                    AppName = first.AppName,
                    ExecutablePath = first.ExecutablePath,
                    ProcessName = first.ProcessName,
                    Timestamp = dailyTimestamp,
                    Granularity = UsageGranularity.Daily,
                    BytesReceived = totalReceived,
                    BytesSent = totalSent,
                    PeakDownloadSpeed = peakDown,
                    PeakUploadSpeed = peakUp,
                    LastUpdated = DateTime.Now
                });
            }
            else
            {
                existingDaily.BytesReceived += totalReceived;
                existingDaily.BytesSent += totalSent;
                existingDaily.PeakDownloadSpeed = Math.Max(existingDaily.PeakDownloadSpeed, peakDown);
                existingDaily.PeakUploadSpeed = Math.Max(existingDaily.PeakUploadSpeed, peakUp);
                existingDaily.LastUpdated = DateTime.Now;
            }
        }

        db.AppUsageRecords.RemoveRange(hourlyRecords);
        await db.SaveChangesAsync();
    }

    public async Task CleanupOldAppDataAsync(int retentionDays)
    {
        if (retentionDays <= 0)
            return;

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var cutoffDate = DateTime.Now.AddDays(-retentionDays);
        await db.AppUsageRecords.Where(a => a.Timestamp < cutoffDate).ExecuteDeleteAsync();
    }
}
