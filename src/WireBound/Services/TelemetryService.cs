using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WireBound.Data;
using WireBound.Models;

namespace WireBound.Services;

/// <summary>
/// Implementation of telemetry and summary computation service
/// </summary>
public class TelemetryService : ITelemetryService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TelemetryService> _logger;

    public TelemetryService(IServiceProvider serviceProvider, ILogger<TelemetryService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<UsageSummary> GetSummaryAsync(DateOnly startDate, DateOnly endDate, SummaryPeriod periodType)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var dailyData = await db.DailyUsages
            .Where(d => d.Date >= startDate && d.Date <= endDate)
            .ToListAsync();

        if (!dailyData.Any())
        {
            return new UsageSummary
            {
                PeriodStart = startDate,
                PeriodEnd = endDate,
                PeriodType = periodType
            };
        }

        var totalReceived = dailyData.Sum(d => d.BytesReceived);
        var totalSent = dailyData.Sum(d => d.BytesSent);
        var activeDays = dailyData.Count;
        var daySpan = (endDate.ToDateTime(TimeOnly.MinValue) - startDate.ToDateTime(TimeOnly.MinValue)).Days + 1;

        var peakDay = dailyData.OrderByDescending(d => d.BytesReceived + d.BytesSent).First();

        // Get hourly data for peak hour analysis
        var startDateTime = startDate.ToDateTime(TimeOnly.MinValue);
        var endDateTime = endDate.ToDateTime(TimeOnly.MaxValue);
        var hourlyData = await db.HourlyUsages
            .Where(h => h.Hour >= startDateTime && h.Hour <= endDateTime)
            .ToListAsync();

        var hourlyTotals = hourlyData
            .GroupBy(h => h.Hour.Hour)
            .Select(g => new { Hour = g.Key, Total = g.Sum(x => x.BytesReceived + x.BytesSent) })
            .OrderByDescending(x => x.Total)
            .FirstOrDefault();

        var dayOfWeekTotals = dailyData
            .GroupBy(d => d.Date.DayOfWeek)
            .Select(g => new { Day = g.Key, Total = g.Sum(x => x.BytesReceived + x.BytesSent) })
            .OrderByDescending(x => x.Total)
            .FirstOrDefault();

        return new UsageSummary
        {
            PeriodStart = startDate,
            PeriodEnd = endDate,
            PeriodType = periodType,
            TotalReceived = totalReceived,
            TotalSent = totalSent,
            AverageDailyBytes = daySpan > 0 ? (totalReceived + totalSent) / daySpan : 0,
            PeakDownloadSpeed = dailyData.Max(d => d.PeakDownloadSpeed),
            PeakUploadSpeed = dailyData.Max(d => d.PeakUploadSpeed),
            PeakUsageDate = peakDay.Date,
            PeakUsageDayBytes = peakDay.BytesReceived + peakDay.BytesSent,
            ActiveDays = activeDays,
            MostActiveHour = hourlyTotals?.Hour ?? 0,
            MostActiveDay = dayOfWeekTotals?.Day ?? DayOfWeek.Monday
        };
    }

    public async Task<UsageSummary> GetTodaySummaryAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return await GetSummaryAsync(today, today, SummaryPeriod.Daily);
    }

    public async Task<UsageSummary> GetThisWeekSummaryAsync()
    {
        var today = DateTime.Today;
        var daysSinceMonday = ((int)today.DayOfWeek - 1 + 7) % 7;
        var monday = DateOnly.FromDateTime(today.AddDays(-daysSinceMonday));
        return await GetSummaryAsync(monday, DateOnly.FromDateTime(today), SummaryPeriod.Weekly);
    }

    public async Task<UsageSummary> GetThisMonthSummaryAsync()
    {
        var today = DateTime.Today;
        var firstOfMonth = new DateOnly(today.Year, today.Month, 1);
        return await GetSummaryAsync(firstOfMonth, DateOnly.FromDateTime(today), SummaryPeriod.Monthly);
    }

    public async Task<List<HourlyUsage>> GetHourlyBreakdownAsync(DateOnly date)
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

    public async Task<List<WeeklyUsage>> GetWeeklyUsageAsync(DateOnly startDate, DateOnly endDate)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        return await db.WeeklyUsages
            .Where(w => w.WeekStart >= startDate && w.WeekStart <= endDate)
            .OrderBy(w => w.WeekStart)
            .ToListAsync();
    }

    public async Task<(UsageSummary current, UsageSummary previous)> GetTrendComparisonAsync(SummaryPeriod period)
    {
        var today = DateTime.Today;
        DateOnly currentStart, currentEnd, previousStart, previousEnd;

        switch (period)
        {
            case SummaryPeriod.Daily:
                currentStart = currentEnd = DateOnly.FromDateTime(today);
                previousStart = previousEnd = DateOnly.FromDateTime(today.AddDays(-1));
                break;

            case SummaryPeriod.Weekly:
                var daysSinceMonday = ((int)today.DayOfWeek - 1 + 7) % 7;
                currentStart = DateOnly.FromDateTime(today.AddDays(-daysSinceMonday));
                currentEnd = DateOnly.FromDateTime(today);
                previousStart = currentStart.AddDays(-7);
                previousEnd = currentStart.AddDays(-1);
                break;

            case SummaryPeriod.Monthly:
                currentStart = new DateOnly(today.Year, today.Month, 1);
                currentEnd = DateOnly.FromDateTime(today);
                var prevMonth = today.AddMonths(-1);
                previousStart = new DateOnly(prevMonth.Year, prevMonth.Month, 1);
                previousEnd = currentStart.AddDays(-1);
                break;

            default:
                throw new ArgumentException($"Unsupported period type: {period}");
        }

        var current = await GetSummaryAsync(currentStart, currentEnd, period);
        var previous = await GetSummaryAsync(previousStart, previousEnd, period);

        // Calculate percentage change
        if (previous.TotalBytes > 0)
        {
            current.ChangeFromPreviousPeriod = ((double)(current.TotalBytes - previous.TotalBytes) / previous.TotalBytes) * 100;
        }

        return (current, previous);
    }

    public async Task<(DateOnly peakDate, long peakBytes, int peakHour)> GetPeakUsageAsync(DateOnly startDate, DateOnly endDate)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var peakDay = await db.DailyUsages
            .Where(d => d.Date >= startDate && d.Date <= endDate)
            .OrderByDescending(d => d.BytesReceived + d.BytesSent)
            .FirstOrDefaultAsync();

        var startDateTime = startDate.ToDateTime(TimeOnly.MinValue);
        var endDateTime = endDate.ToDateTime(TimeOnly.MaxValue);
        var peakHourData = await db.HourlyUsages
            .Where(h => h.Hour >= startDateTime && h.Hour <= endDateTime)
            .GroupBy(h => h.Hour.Hour)
            .Select(g => new { Hour = g.Key, Total = g.Sum(x => x.BytesReceived + x.BytesSent) })
            .OrderByDescending(x => x.Total)
            .FirstOrDefaultAsync();

        return (
            peakDay?.Date ?? startDate,
            peakDay != null ? peakDay.BytesReceived + peakDay.BytesSent : 0,
            peakHourData?.Hour ?? 0
        );
    }

    public async Task LogEventAsync(TelemetryCategory category, string eventType, string description, long? value = null, string? adapterId = null, string? metadata = null)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

            var telemetryEvent = new TelemetryEvent
            {
                Timestamp = DateTime.Now,
                Category = category,
                EventType = eventType,
                Description = description,
                Value = value,
                AdapterId = adapterId,
                Metadata = metadata
            };

            db.TelemetryEvents.Add(telemetryEvent);
            await db.SaveChangesAsync();

            _logger.LogDebug("Telemetry event logged: [{Category}] {EventType} - {Description}", category, eventType, description);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log telemetry event: {EventType}", eventType);
        }
    }

    public async Task<List<TelemetryEvent>> GetRecentEventsAsync(int count = 50, TelemetryCategory? category = null)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var query = db.TelemetryEvents.AsQueryable();

        if (category.HasValue)
        {
            query = query.Where(e => e.Category == category.Value);
        }

        return await query
            .OrderByDescending(e => e.Timestamp)
            .Take(count)
            .ToListAsync();
    }

    public async Task<Dictionary<DayOfWeek, long>> GetUsageByDayOfWeekAsync(DateOnly startDate, DateOnly endDate)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var dailyData = await db.DailyUsages
            .Where(d => d.Date >= startDate && d.Date <= endDate)
            .ToListAsync();

        return dailyData
            .GroupBy(d => d.Date.DayOfWeek)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(d => d.BytesReceived + d.BytesSent) / g.Count()
            );
    }

    public async Task<Dictionary<int, long>> GetUsageByHourOfDayAsync(DateOnly startDate, DateOnly endDate)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var startDateTime = startDate.ToDateTime(TimeOnly.MinValue);
        var endDateTime = endDate.ToDateTime(TimeOnly.MaxValue);

        var hourlyData = await db.HourlyUsages
            .Where(h => h.Hour >= startDateTime && h.Hour <= endDateTime)
            .ToListAsync();

        var result = new Dictionary<int, long>();
        for (int i = 0; i < 24; i++)
        {
            var hourData = hourlyData.Where(h => h.Hour.Hour == i).ToList();
            result[i] = hourData.Any() 
                ? hourData.Sum(h => h.BytesReceived + h.BytesSent) / hourData.Count 
                : 0;
        }

        return result;
    }

    public async Task AggregateWeeklyDataAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        // Get the last week that was fully aggregated
        var lastAggregated = await db.WeeklyUsages
            .OrderByDescending(w => w.WeekStart)
            .FirstOrDefaultAsync();

        var startDate = lastAggregated?.WeekStart.AddDays(7) ?? DateOnly.FromDateTime(DateTime.Today.AddDays(-365));
        var endDate = DateOnly.FromDateTime(DateTime.Today);

        // Only aggregate complete weeks (not the current week)
        var today = DateTime.Today;
        var daysSinceMonday = ((int)today.DayOfWeek - 1 + 7) % 7;
        var currentWeekStart = DateOnly.FromDateTime(today.AddDays(-daysSinceMonday));

        var dailyData = await db.DailyUsages
            .Where(d => d.Date >= startDate && d.Date < currentWeekStart)
            .OrderBy(d => d.Date)
            .ToListAsync();

        if (!dailyData.Any()) return;

        // Group by week
        var weeklyGroups = dailyData
            .GroupBy(d =>
            {
                var date = d.Date.ToDateTime(TimeOnly.MinValue);
                var days = ((int)date.DayOfWeek - 1 + 7) % 7;
                return DateOnly.FromDateTime(date.AddDays(-days));
            });

        foreach (var week in weeklyGroups)
        {
            var weekStart = week.Key;
            var year = weekStart.Year;
            var weekNumber = ISOWeek.GetWeekOfYear(weekStart.ToDateTime(TimeOnly.MinValue));

            var adapters = week.Select(d => d.AdapterId).Distinct();

            foreach (var adapterId in adapters)
            {
                var adapterData = week.Where(d => d.AdapterId == adapterId).ToList();

                var existing = await db.WeeklyUsages
                    .FirstOrDefaultAsync(w => w.WeekStart == weekStart && w.AdapterId == adapterId);

                if (existing == null)
                {
                    var weeklyRecord = new WeeklyUsage
                    {
                        WeekStart = weekStart,
                        Year = year,
                        WeekNumber = weekNumber,
                        AdapterId = adapterId,
                        BytesReceived = adapterData.Sum(d => d.BytesReceived),
                        BytesSent = adapterData.Sum(d => d.BytesSent),
                        PeakDownloadSpeed = adapterData.Max(d => d.PeakDownloadSpeed),
                        PeakUploadSpeed = adapterData.Max(d => d.PeakUploadSpeed),
                        ActiveDays = adapterData.Count,
                        LastUpdated = DateTime.Now
                    };
                    db.WeeklyUsages.Add(weeklyRecord);
                }
            }
        }

        await db.SaveChangesAsync();
        _logger.LogInformation("Aggregated weekly data for {WeekCount} weeks", weeklyGroups.Count());
    }

    public async Task CleanupOldEventsAsync(int retentionDays)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var cutoffDate = DateTime.Now.AddDays(-retentionDays);
        await db.TelemetryEvents.Where(e => e.Timestamp < cutoffDate).ExecuteDeleteAsync();

        _logger.LogInformation("Cleaned up telemetry events older than {RetentionDays} days", retentionDays);
    }
}
