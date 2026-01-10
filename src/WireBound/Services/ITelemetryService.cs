using WireBound.Models;

namespace WireBound.Services;

/// <summary>
/// Service for computing usage summaries and telemetry insights
/// </summary>
public interface ITelemetryService
{
    /// <summary>
    /// Get usage summary for a specific period
    /// </summary>
    Task<UsageSummary> GetSummaryAsync(DateOnly startDate, DateOnly endDate, SummaryPeriod periodType);

    /// <summary>
    /// Get today's summary
    /// </summary>
    Task<UsageSummary> GetTodaySummaryAsync();

    /// <summary>
    /// Get this week's summary (Monday to today)
    /// </summary>
    Task<UsageSummary> GetThisWeekSummaryAsync();

    /// <summary>
    /// Get this month's summary
    /// </summary>
    Task<UsageSummary> GetThisMonthSummaryAsync();

    /// <summary>
    /// Get hourly breakdown for a specific date
    /// </summary>
    Task<List<HourlyUsage>> GetHourlyBreakdownAsync(DateOnly date);

    /// <summary>
    /// Get weekly usage data for a date range
    /// </summary>
    Task<List<WeeklyUsage>> GetWeeklyUsageAsync(DateOnly startDate, DateOnly endDate);

    /// <summary>
    /// Get usage trend comparison (current vs previous period)
    /// </summary>
    Task<(UsageSummary current, UsageSummary previous)> GetTrendComparisonAsync(SummaryPeriod period);

    /// <summary>
    /// Get peak usage statistics
    /// </summary>
    Task<(DateOnly peakDate, long peakBytes, int peakHour)> GetPeakUsageAsync(DateOnly startDate, DateOnly endDate);

    /// <summary>
    /// Log a telemetry event
    /// </summary>
    Task LogEventAsync(TelemetryCategory category, string eventType, string description, long? value = null, string? adapterId = null, string? metadata = null);

    /// <summary>
    /// Get recent telemetry events
    /// </summary>
    Task<List<TelemetryEvent>> GetRecentEventsAsync(int count = 50, TelemetryCategory? category = null);

    /// <summary>
    /// Get usage by day of week (average)
    /// </summary>
    Task<Dictionary<DayOfWeek, long>> GetUsageByDayOfWeekAsync(DateOnly startDate, DateOnly endDate);

    /// <summary>
    /// Get usage by hour of day (average)
    /// </summary>
    Task<Dictionary<int, long>> GetUsageByHourOfDayAsync(DateOnly startDate, DateOnly endDate);

    /// <summary>
    /// Aggregate daily usage into weekly records
    /// </summary>
    Task AggregateWeeklyDataAsync();

    /// <summary>
    /// Clean up old telemetry events
    /// </summary>
    Task CleanupOldEventsAsync(int retentionDays);
}
