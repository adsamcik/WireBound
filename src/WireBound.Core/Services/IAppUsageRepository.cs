using WireBound.Core.Models;
using WireBound.Platform.Abstract.Models;

namespace WireBound.Core.Services;

/// <summary>
/// Repository for per-application network usage tracking.
/// </summary>
public interface IAppUsageRepository
{
    /// <summary>
    /// Save per-app network statistics.
    /// </summary>
    Task SaveAppStatsAsync(IEnumerable<ProcessNetworkStats> stats);

    /// <summary>
    /// Get per-app usage records for a specific app in a date range.
    /// </summary>
    Task<List<AppUsageRecord>> GetAppUsageAsync(
        string appIdentifier,
        DateOnly startDate,
        DateOnly endDate,
        UsageGranularity? granularity = null);

    /// <summary>
    /// Get all per-app usage records for a date range.
    /// </summary>
    Task<List<AppUsageRecord>> GetAllAppUsageAsync(
        DateOnly startDate,
        DateOnly endDate,
        UsageGranularity? granularity = null);

    /// <summary>
    /// Get top applications by usage for a date range.
    /// </summary>
    Task<List<AppUsageRecord>> GetTopAppsAsync(int count, DateOnly startDate, DateOnly endDate);

    /// <summary>
    /// Aggregate hourly app data to daily for records older than specified days.
    /// </summary>
    Task AggregateAppDataAsync(int olderThanDays);

    /// <summary>
    /// Clean up old app data beyond retention period.
    /// </summary>
    Task CleanupOldAppDataAsync(int retentionDays);
}
