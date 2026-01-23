using WireBound.Core.Models;

namespace WireBound.Core.Services;

/// <summary>
/// Service for persisting and querying historical system statistics (CPU, Memory, GPU)
/// </summary>
public interface ISystemHistoryService
{
    /// <summary>
    /// Records current system stats sample for aggregation
    /// </summary>
    Task RecordSampleAsync(SystemStats stats);
    
    /// <summary>
    /// Gets hourly system stats for a date range
    /// </summary>
    Task<IReadOnlyList<HourlySystemStats>> GetHourlyStatsAsync(DateTime start, DateTime end);
    
    /// <summary>
    /// Gets daily system stats for a date range
    /// </summary>
    Task<IReadOnlyList<DailySystemStats>> GetDailyStatsAsync(DateOnly start, DateOnly end);
    
    /// <summary>
    /// Gets the average CPU usage for a time period
    /// </summary>
    Task<double> GetAverageCpuAsync(DateTime start, DateTime end);
    
    /// <summary>
    /// Gets the average memory usage for a time period
    /// </summary>
    Task<double> GetAverageMemoryAsync(DateTime start, DateTime end);
    
    /// <summary>
    /// Aggregates samples into hourly stats (called periodically)
    /// </summary>
    Task AggregateHourlyAsync();
    
    /// <summary>
    /// Aggregates hourly stats into daily stats (called daily)
    /// </summary>
    Task AggregateDailyAsync();
}
