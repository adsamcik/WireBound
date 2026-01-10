using WireBound.Models;

namespace WireBound.Services;

/// <summary>
/// Service for persisting network data to database
/// </summary>
public interface IDataPersistenceService
{
    /// <summary>
    /// Save current stats to the database
    /// </summary>
    Task SaveStatsAsync(NetworkStats stats);

    /// <summary>
    /// Get daily usage for a date range
    /// </summary>
    Task<List<DailyUsage>> GetDailyUsageAsync(DateOnly startDate, DateOnly endDate);

    /// <summary>
    /// Get hourly usage for a specific date
    /// </summary>
    Task<List<HourlyUsage>> GetHourlyUsageAsync(DateOnly date);

    /// <summary>
    /// Get hourly usage for a date range
    /// </summary>
    Task<List<HourlyUsage>> GetHourlyUsageRangeAsync(DateOnly startDate, DateOnly endDate);

    /// <summary>
    /// Get weekly usage for a date range
    /// </summary>
    Task<List<WeeklyUsage>> GetWeeklyUsageAsync(DateOnly startDate, DateOnly endDate);

    /// <summary>
    /// Get total usage statistics
    /// </summary>
    Task<(long totalReceived, long totalSent)> GetTotalUsageAsync();

    /// <summary>
    /// Get usage for today
    /// </summary>
    Task<(long received, long sent)> GetTodayUsageAsync();

    /// <summary>
    /// Get usage for this week (Monday to today)
    /// </summary>
    Task<(long received, long sent)> GetThisWeekUsageAsync();

    /// <summary>
    /// Get usage for this month
    /// </summary>
    Task<(long received, long sent)> GetThisMonthUsageAsync();

    /// <summary>
    /// Clean up old data beyond retention period
    /// </summary>
    Task CleanupOldDataAsync(int retentionDays);

    /// <summary>
    /// Get app settings
    /// </summary>
    Task<AppSettings> GetSettingsAsync();

    /// <summary>
    /// Save app settings
    /// </summary>
    Task SaveSettingsAsync(AppSettings settings);
    
    // === Per-App Network Tracking Methods ===
    
    /// <summary>
    /// Save per-app network statistics
    /// </summary>
    Task SaveAppStatsAsync(IEnumerable<ProcessNetworkStats> stats);
    
    /// <summary>
    /// Get the top apps by total usage within a date range
    /// </summary>
    /// <param name="count">Number of top apps to return</param>
    /// <param name="startDate">Start date (inclusive)</param>
    /// <param name="endDate">End date (inclusive)</param>
    Task<List<AppUsageRecord>> GetTopAppsAsync(int count, DateOnly startDate, DateOnly endDate);
    
    /// <summary>
    /// Get usage history for a specific app
    /// </summary>
    /// <param name="appIdentifier">The app's unique identifier</param>
    /// <param name="startDate">Start date (inclusive)</param>
    /// <param name="endDate">End date (inclusive)</param>
    /// <param name="granularity">Granularity level (Hourly or Daily)</param>
    Task<List<AppUsageRecord>> GetAppUsageAsync(string appIdentifier, DateOnly startDate, DateOnly endDate, UsageGranularity? granularity = null);
    
    /// <summary>
    /// Get all apps with usage data within a date range
    /// </summary>
    Task<List<AppUsageRecord>> GetAllAppUsageAsync(DateOnly startDate, DateOnly endDate, UsageGranularity? granularity = null);
    
    /// <summary>
    /// Aggregate old hourly app data to daily records
    /// </summary>
    /// <param name="aggregateAfterDays">Aggregate hourly records older than this many days</param>
    Task AggregateAppDataAsync(int aggregateAfterDays);
    
    /// <summary>
    /// Clean up old per-app data beyond retention period
    /// </summary>
    /// <param name="retentionDays">Days to retain (0 = indefinite, no cleanup)</param>
    Task CleanupOldAppDataAsync(int retentionDays);
}
