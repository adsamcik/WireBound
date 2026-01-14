using WireBound.Core.Models;

namespace WireBound.Core.Services;

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
    /// Get total usage statistics
    /// </summary>
    Task<(long totalReceived, long totalSent)> GetTotalUsageAsync();

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
    /// Get per-app usage records for a specific app in a date range
    /// </summary>
    Task<List<AppUsageRecord>> GetAppUsageAsync(string appIdentifier, DateOnly startDate, DateOnly endDate, UsageGranularity? granularity = null);

    /// <summary>
    /// Get all per-app usage records for a date range
    /// </summary>
    Task<List<AppUsageRecord>> GetAllAppUsageAsync(DateOnly startDate, DateOnly endDate, UsageGranularity? granularity = null);

    /// <summary>
    /// Get top applications by usage for a date range
    /// </summary>
    Task<List<AppUsageRecord>> GetTopAppsAsync(int count, DateOnly startDate, DateOnly endDate);

    /// <summary>
    /// Aggregate hourly app data to daily for records older than specified days
    /// </summary>
    Task AggregateAppDataAsync(int olderThanDays);

    /// <summary>
    /// Clean up old app data beyond retention period
    /// </summary>
    Task CleanupOldAppDataAsync(int retentionDays);
}
