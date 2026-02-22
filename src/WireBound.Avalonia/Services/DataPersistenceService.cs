using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WireBound.Core.Data;
using WireBound.Core.Models;
using WireBound.Core.Services;
using WireBound.Platform.Abstract.Models;

namespace WireBound.Avalonia.Services;

/// <summary>
/// Service for persisting network statistics to the database.
/// Thread-safe: uses async locking to prevent concurrent save operations from corrupting data.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Trimming", "IL2026", Justification = "EF Core LINQ queries use expression trees that may require unreferenced code; works at runtime")]
public sealed class DataPersistenceService : IDataPersistenceService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private long _lastSavedReceived;
    private long _lastSavedSent;

    public DataPersistenceService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task SaveStatsAsync(NetworkStats stats)
    {
        // Use async lock to serialize save operations and prevent race conditions
        // where concurrent calls calculate deltas before either writes to the database
        await _saveLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await SaveStatsInternalAsync(stats).ConfigureAwait(false);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private async Task SaveStatsInternalAsync(NetworkStats stats)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var now = DateTime.Now;
        var currentHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);
        var today = DateOnly.FromDateTime(now);

        // Calculate delta since last save (now protected by async lock)
        var receivedDelta = stats.SessionBytesReceived - _lastSavedReceived;
        var sentDelta = stats.SessionBytesSent - _lastSavedSent;

        if (receivedDelta < 0) receivedDelta = stats.SessionBytesReceived;
        if (sentDelta < 0) sentDelta = stats.SessionBytesSent;

        _lastSavedReceived = stats.SessionBytesReceived;
        _lastSavedSent = stats.SessionBytesSent;

        // Update or create hourly record
        var hourlyRecord = await db.HourlyUsages
            .FirstOrDefaultAsync(h => h.Hour == currentHour && h.AdapterId == stats.AdapterId)
            .ConfigureAwait(false);

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
            .FirstOrDefaultAsync(d => d.Date == today && d.AdapterId == stats.AdapterId)
            .ConfigureAwait(false);

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

        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    public async Task<List<DailyUsage>> GetDailyUsageAsync(DateOnly startDate, DateOnly endDate)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        return await db.DailyUsages
            .AsNoTracking()
            .Where(d => d.Date >= startDate && d.Date <= endDate)
            .OrderBy(d => d.Date)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    public async Task<List<HourlyUsage>> GetHourlyUsageAsync(DateOnly date)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var startOfDay = date.ToDateTime(TimeOnly.MinValue);
        var endOfDay = date.ToDateTime(TimeOnly.MaxValue);

        return await db.HourlyUsages
            .AsNoTracking()
            .Where(h => h.Hour >= startOfDay && h.Hour <= endOfDay)
            .OrderBy(h => h.Hour)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    public async Task<(long totalReceived, long totalSent)> GetTotalUsageAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var totals = await db.DailyUsages
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                TotalReceived = g.Sum(d => d.BytesReceived),
                TotalSent = g.Sum(d => d.BytesSent)
            })
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        return (totals?.TotalReceived ?? 0, totals?.TotalSent ?? 0);
    }

    public async Task<(long totalReceived, long totalSent)> GetTodayUsageAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var today = DateOnly.FromDateTime(DateTime.Now);
        var totals = await db.DailyUsages
            .AsNoTracking()
            .Where(d => d.Date == today)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                TotalReceived = g.Sum(d => d.BytesReceived),
                TotalSent = g.Sum(d => d.BytesSent)
            })
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        return (totals?.TotalReceived ?? 0, totals?.TotalSent ?? 0);
    }

    public async Task<Dictionary<string, (long received, long sent)>> GetTodayUsageByAdapterAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var today = DateOnly.FromDateTime(DateTime.Now);
        var usages = await db.DailyUsages
            .AsNoTracking()
            .Where(d => d.Date == today)
            .ToListAsync()
            .ConfigureAwait(false);

        return usages.ToDictionary(
            u => u.AdapterId,
            u => (u.BytesReceived, u.BytesSent)
        );
    }

    public async Task CleanupOldDataAsync(int retentionDays)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var cutoffDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-retentionDays));
        var cutoffDateTime = cutoffDate.ToDateTime(TimeOnly.MinValue);

        await db.DailyUsages.Where(d => d.Date < cutoffDate).ExecuteDeleteAsync().ConfigureAwait(false);
        await db.HourlyUsages.Where(h => h.Hour < cutoffDateTime).ExecuteDeleteAsync().ConfigureAwait(false);
    }

    public async Task<AppSettings> GetSettingsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var settings = await db.Settings.AsNoTracking().FirstOrDefaultAsync().ConfigureAwait(false);
        return settings ?? new AppSettings();
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var existing = await db.Settings.FirstOrDefaultAsync().ConfigureAwait(false);
        if (existing == null)
        {
            settings.Id = 1;
            db.Settings.Add(settings);
        }
        else
        {
            db.Entry(existing).CurrentValues.SetValues(settings);
        }

        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    // === Per-App Network Tracking Methods ===

    public async Task SaveAppStatsAsync(IEnumerable<ProcessNetworkStats> stats)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var now = DateTime.Now;
        var currentHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);

        var statsList = stats.Where(s => !string.IsNullOrEmpty(s.AppIdentifier)).ToList();
        if (statsList.Count == 0)
            return;

        // Batch-load all existing hourly records for these apps to avoid N+1 queries
        var appIds = statsList.Select(s => s.AppIdentifier).Distinct().ToList();
        var existingRecords = await db.AppUsageRecords
            .Where(a =>
                a.Timestamp == currentHour &&
                appIds.Contains(a.AppIdentifier) &&
                a.Granularity == UsageGranularity.Hourly)
            .ToDictionaryAsync(a => a.AppIdentifier)
            .ConfigureAwait(false);

        foreach (var stat in statsList)
        {
            existingRecords.TryGetValue(stat.AppIdentifier, out var record);

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
                // If session bytes decreased, the process restarted — accumulate
                // the new session's bytes on top of what was already recorded.
                // If session bytes increased, it's the same session — take the higher value.
                if (stat.SessionBytesReceived < record.BytesReceived)
                    record.BytesReceived += stat.SessionBytesReceived;
                else
                    record.BytesReceived = Math.Max(record.BytesReceived, stat.SessionBytesReceived);

                if (stat.SessionBytesSent < record.BytesSent)
                    record.BytesSent += stat.SessionBytesSent;
                else
                    record.BytesSent = Math.Max(record.BytesSent, stat.SessionBytesSent);

                record.PeakDownloadSpeed = Math.Max(record.PeakDownloadSpeed, stat.DownloadSpeedBps);
                record.PeakUploadSpeed = Math.Max(record.PeakUploadSpeed, stat.UploadSpeedBps);
                record.LastUpdated = now;
            }
        }

        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    public async Task<List<AppUsageRecord>> GetTopAppsAsync(int count, DateOnly startDate, DateOnly endDate)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var startDateTime = startDate.ToDateTime(TimeOnly.MinValue);
        var endDateTime = endDate.ToDateTime(TimeOnly.MaxValue);

        var topApps = await db.AppUsageRecords
            .AsNoTracking()
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
            .ToListAsync()
            .ConfigureAwait(false);

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
            .AsNoTracking()
            .Where(a => a.AppIdentifier == appIdentifier &&
                        a.Timestamp >= startDateTime &&
                        a.Timestamp <= endDateTime);

        if (granularity.HasValue)
        {
            query = query.Where(a => a.Granularity == granularity.Value);
        }

        return await query
            .OrderBy(a => a.Timestamp)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    public async Task<List<AppUsageRecord>> GetAllAppUsageAsync(DateOnly startDate, DateOnly endDate, UsageGranularity? granularity = null)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var startDateTime = startDate.ToDateTime(TimeOnly.MinValue);
        var endDateTime = endDate.ToDateTime(TimeOnly.MaxValue);

        var query = db.AppUsageRecords
            .AsNoTracking()
            .Where(a => a.Timestamp >= startDateTime && a.Timestamp <= endDateTime);

        if (granularity.HasValue)
        {
            query = query.Where(a => a.Granularity == granularity.Value);
        }

        return await query
            .OrderByDescending(a => a.BytesReceived + a.BytesSent)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    public async Task AggregateAppDataAsync(int aggregateAfterDays)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var cutoffDate = DateTime.Now.AddDays(-aggregateAfterDays);
        // Limit how far back we load to prevent unbounded memory usage
        var oldestDate = cutoffDate.AddDays(-30);

        var hourlyRecords = await db.AppUsageRecords
            .Where(a => a.Granularity == UsageGranularity.Hourly &&
                        a.Timestamp >= oldestDate &&
                        a.Timestamp < cutoffDate)
            .ToListAsync()
            .ConfigureAwait(false);

        if (hourlyRecords.Count == 0)
            return;

        // Wrap upsert + delete in a transaction to prevent data corruption on partial failure
        await using var transaction = await db.Database.BeginTransactionAsync().ConfigureAwait(false);
        try
        {
            var groupedByDay = hourlyRecords
                .GroupBy(a => new { a.AppIdentifier, Date = DateOnly.FromDateTime(a.Timestamp) });

            // Batch-load all existing daily records to avoid N+1 queries
            var allDailyTimestamps = groupedByDay
                .Select(g => g.Key.Date.ToDateTime(TimeOnly.MinValue))
                .Distinct()
                .ToList();
            var allAppIds = groupedByDay.Select(g => g.Key.AppIdentifier).Distinct().ToList();
            var existingDailyRecords = await db.AppUsageRecords
                .Where(a =>
                    a.Granularity == UsageGranularity.Daily &&
                    allAppIds.Contains(a.AppIdentifier) &&
                    allDailyTimestamps.Contains(a.Timestamp))
                .ToListAsync()
                .ConfigureAwait(false);
            var dailyLookup = existingDailyRecords
                .ToDictionary(r => (r.AppIdentifier, r.Timestamp));

            foreach (var group in groupedByDay)
            {
                var dailyTimestamp = group.Key.Date.ToDateTime(TimeOnly.MinValue);

                dailyLookup.TryGetValue((group.Key.AppIdentifier, dailyTimestamp), out var existingDaily);

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
            await db.SaveChangesAsync().ConfigureAwait(false);
            await transaction.CommitAsync().ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task CleanupOldAppDataAsync(int retentionDays)
    {
        if (retentionDays <= 0)
            return;

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var cutoffDate = DateTime.Now.AddDays(-retentionDays);
        await db.AppUsageRecords.Where(a => a.Timestamp < cutoffDate).ExecuteDeleteAsync().ConfigureAwait(false);
    }

    public async Task SaveSpeedSnapshotAsync(long downloadSpeedBps, long uploadSpeedBps)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        db.SpeedSnapshots.Add(new SpeedSnapshot
        {
            Timestamp = DateTime.Now,
            DownloadSpeedBps = downloadSpeedBps,
            UploadSpeedBps = uploadSpeedBps
        });

        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    public async Task SaveSpeedSnapshotBatchAsync(IEnumerable<(long downloadBps, long uploadBps, DateTime timestamp)> snapshots)
    {
        var snapshotList = snapshots.ToList();
        if (snapshotList.Count == 0)
            return;

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var entities = snapshotList.Select(s => new SpeedSnapshot
        {
            Timestamp = s.timestamp,
            DownloadSpeedBps = s.downloadBps,
            UploadSpeedBps = s.uploadBps
        });

        db.SpeedSnapshots.AddRange(entities);
        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    public async Task<List<SpeedSnapshot>> GetSpeedHistoryAsync(DateTime since)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        return await db.SpeedSnapshots
            .AsNoTracking()
            .Where(s => s.Timestamp >= since)
            .OrderBy(s => s.Timestamp)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    public async Task CleanupOldSpeedSnapshotsAsync(TimeSpan maxAge)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var cutoff = DateTime.Now - maxAge;
        await db.SpeedSnapshots
            .Where(s => s.Timestamp < cutoff)
            .ExecuteDeleteAsync()
            .ConfigureAwait(false);
    }
}
