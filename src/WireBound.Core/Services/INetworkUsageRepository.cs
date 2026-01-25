using WireBound.Core.Models;

namespace WireBound.Core.Services;

/// <summary>
/// Repository for network usage data persistence.
/// Handles daily and hourly usage tracking per network adapter.
/// </summary>
public interface INetworkUsageRepository
{
    /// <summary>
    /// Save current stats to the database.
    /// </summary>
    Task SaveStatsAsync(NetworkStats stats);

    /// <summary>
    /// Get daily usage for a date range.
    /// </summary>
    Task<List<DailyUsage>> GetDailyUsageAsync(DateOnly startDate, DateOnly endDate);

    /// <summary>
    /// Get hourly usage for a specific date.
    /// </summary>
    Task<List<HourlyUsage>> GetHourlyUsageAsync(DateOnly date);

    /// <summary>
    /// Get total usage statistics since tracking began.
    /// </summary>
    Task<(long totalReceived, long totalSent)> GetTotalUsageAsync();

    /// <summary>
    /// Get today's usage statistics.
    /// </summary>
    Task<(long totalReceived, long totalSent)> GetTodayUsageAsync();

    /// <summary>
    /// Get today's usage statistics per adapter.
    /// </summary>
    Task<Dictionary<string, (long received, long sent)>> GetTodayUsageByAdapterAsync();

    /// <summary>
    /// Clean up old network data beyond retention period.
    /// </summary>
    Task CleanupOldDataAsync(int retentionDays);
}
