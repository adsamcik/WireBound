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
    /// Get the top apps by total usage within a date range
    /// </summary>
    Task<List<AppUsageRecord>> GetTopAppsAsync(int count, DateOnly startDate, DateOnly endDate);
    
    /// <summary>
    /// Get usage history for a specific app
    /// </summary>
    Task<List<AppUsageRecord>> GetAppUsageAsync(string appIdentifier, DateOnly startDate, DateOnly endDate, UsageGranularity? granularity = null);
    
    /// <summary>
    /// Get all apps with usage data within a date range
    /// </summary>
    Task<List<AppUsageRecord>> GetAllAppUsageAsync(DateOnly startDate, DateOnly endDate, UsageGranularity? granularity = null);
    
    /// <summary>
    /// Aggregate old hourly app data to daily records
    /// </summary>
    Task AggregateAppDataAsync(int aggregateAfterDays);
    
    /// <summary>
    /// Clean up old per-app data beyond retention period
    /// </summary>
    Task CleanupOldAppDataAsync(int retentionDays);
}
