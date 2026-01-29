using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WireBound.Core.Data;
using WireBound.Core.Models;
using WireBound.Core.Services;

namespace WireBound.Avalonia.Services;

/// <summary>
/// Service for persisting and querying historical system statistics.
/// Buffers samples in memory and aggregates them into hourly/daily records.
/// </summary>
public sealed class SystemHistoryService : ISystemHistoryService, IDisposable
{
    /// <summary>
    /// Maximum number of samples to keep in the buffer (~2 hours at 1 sample/second).
    /// Prevents unbounded memory growth if aggregation is delayed.
    /// </summary>
    private const int MaxSampleBufferSize = 7200;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SystemHistoryService> _logger;
    private readonly ConcurrentQueue<SystemStatsSample> _sampleBuffer = new();
    private readonly object _aggregationLock = new();
    private DateTime _lastHourlyAggregation = DateTime.MinValue;
    private DateTime _lastDailyAggregation = DateTime.MinValue;
    private bool _disposed;

    /// <summary>
    /// Internal sample structure for buffering raw stats
    /// </summary>
    private readonly record struct SystemStatsSample(
        DateTime Timestamp,
        double CpuPercent,
        double MemoryPercent,
        long MemoryUsedBytes,
        double? GpuPercent,
        double? GpuMemoryPercent);

    public SystemHistoryService(IServiceProvider serviceProvider, ILogger<SystemHistoryService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task RecordSampleAsync(SystemStats stats)
    {
        if (_disposed)
            return Task.CompletedTask;

        try
        {
            var sample = new SystemStatsSample(
                stats.Timestamp,
                stats.Cpu.UsagePercent,
                stats.Memory.UsagePercent,
                stats.Memory.UsedBytes,
                GpuPercent: null, // GPU stats not yet in SystemStats model
                GpuMemoryPercent: null);

            _sampleBuffer.Enqueue(sample);

            // Limit buffer size to prevent memory issues
            while (_sampleBuffer.Count > MaxSampleBufferSize)
            {
                _sampleBuffer.TryDequeue(out _);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record system stats sample");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<HourlySystemStats>> GetHourlyStatsAsync(DateTime start, DateTime end)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        try
        {
            var stats = await db.HourlySystemStats
                .Where(h => h.Hour >= start && h.Hour <= end)
                .OrderBy(h => h.Hour)
                .ToListAsync()
                .ConfigureAwait(false);

            return stats;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get hourly system stats from {Start} to {End}", start, end);
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DailySystemStats>> GetDailyStatsAsync(DateOnly start, DateOnly end)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        try
        {
            var stats = await db.DailySystemStats
                .Where(d => d.Date >= start && d.Date <= end)
                .OrderBy(d => d.Date)
                .ToListAsync()
                .ConfigureAwait(false);

            return stats;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get daily system stats from {Start} to {End}", start, end);
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<double> GetAverageCpuAsync(DateTime start, DateTime end)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        try
        {
            var hasData = await db.HourlySystemStats
                .AnyAsync(h => h.Hour >= start && h.Hour <= end)
                .ConfigureAwait(false);

            if (!hasData)
                return 0;

            return await db.HourlySystemStats
                .Where(h => h.Hour >= start && h.Hour <= end)
                .AverageAsync(h => h.AvgCpuPercent)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get average CPU from {Start} to {End}", start, end);
            return 0;
        }
    }

    /// <inheritdoc />
    public async Task<double> GetAverageMemoryAsync(DateTime start, DateTime end)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        try
        {
            var hasData = await db.HourlySystemStats
                .AnyAsync(h => h.Hour >= start && h.Hour <= end)
                .ConfigureAwait(false);

            if (!hasData)
                return 0;

            return await db.HourlySystemStats
                .Where(h => h.Hour >= start && h.Hour <= end)
                .AverageAsync(h => h.AvgMemoryPercent)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get average memory from {Start} to {End}", start, end);
            return 0;
        }
    }

    /// <inheritdoc />
    public async Task AggregateHourlyAsync()
    {
        if (_disposed)
            return;

        var now = DateTime.Now;
        var currentHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);

        // Only aggregate if we haven't done so for this hour and we have samples
        lock (_aggregationLock)
        {
            if (_lastHourlyAggregation >= currentHour)
                return;
        }

        try
        {
            // Collect samples from the buffer for the previous hour(s)
            var samplesToProcess = new List<SystemStatsSample>();
            var remainingSamples = new List<SystemStatsSample>();

            while (_sampleBuffer.TryDequeue(out var sample))
            {
                var sampleHour = new DateTime(
                    sample.Timestamp.Year,
                    sample.Timestamp.Month,
                    sample.Timestamp.Day,
                    sample.Timestamp.Hour,
                    0, 0);

                if (sampleHour < currentHour)
                {
                    samplesToProcess.Add(sample);
                }
                else
                {
                    // Keep samples from current hour
                    remainingSamples.Add(sample);
                }
            }

            // Re-queue current hour samples
            foreach (var sample in remainingSamples)
            {
                _sampleBuffer.Enqueue(sample);
            }

            if (samplesToProcess.Count == 0)
            {
                lock (_aggregationLock)
                {
                    _lastHourlyAggregation = currentHour;
                }
                return;
            }

            // Group samples by hour and aggregate
            var hourlyGroups = samplesToProcess
                .GroupBy(s => new DateTime(s.Timestamp.Year, s.Timestamp.Month, s.Timestamp.Day, s.Timestamp.Hour, 0, 0))
                .ToList();

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

            foreach (var group in hourlyGroups)
            {
                var hour = group.Key;
                var samples = group.ToList();

                if (samples.Count == 0)
                    continue;

                var existingRecord = await db.HourlySystemStats
                    .FirstOrDefaultAsync(h => h.Hour == hour)
                    .ConfigureAwait(false);

                var cpuValues = samples.Select(s => s.CpuPercent).ToList();
                var memValues = samples.Select(s => s.MemoryPercent).ToList();
                var memBytesValues = samples.Select(s => s.MemoryUsedBytes).ToList();
                var gpuValues = samples.Where(s => s.GpuPercent.HasValue).Select(s => s.GpuPercent!.Value).ToList();
                var gpuMemValues = samples.Where(s => s.GpuMemoryPercent.HasValue).Select(s => s.GpuMemoryPercent!.Value).ToList();

                if (existingRecord == null)
                {
                    var newRecord = new HourlySystemStats
                    {
                        Hour = hour,
                        AvgCpuPercent = cpuValues.Average(),
                        MaxCpuPercent = cpuValues.Max(),
                        MinCpuPercent = cpuValues.Min(),
                        AvgMemoryPercent = memValues.Average(),
                        MaxMemoryPercent = memValues.Max(),
                        AvgMemoryUsedBytes = (long)memBytesValues.Average(),
                        AvgGpuPercent = gpuValues.Count > 0 ? gpuValues.Average() : null,
                        MaxGpuPercent = gpuValues.Count > 0 ? gpuValues.Max() : null,
                        AvgGpuMemoryPercent = gpuMemValues.Count > 0 ? gpuMemValues.Average() : null
                    };

                    db.HourlySystemStats.Add(newRecord);
                    _logger.LogDebug("Created hourly system stats for {Hour} from {Count} samples", hour, samples.Count);
                }
                else
                {
                    // Merge with existing record (weighted average based on sample counts)
                    // For simplicity, we'll just update with new maximums if higher
                    existingRecord.MaxCpuPercent = Math.Max(existingRecord.MaxCpuPercent, cpuValues.Max());
                    existingRecord.MinCpuPercent = Math.Min(existingRecord.MinCpuPercent, cpuValues.Min());
                    existingRecord.MaxMemoryPercent = Math.Max(existingRecord.MaxMemoryPercent, memValues.Max());

                    if (gpuValues.Count > 0)
                    {
                        existingRecord.MaxGpuPercent = existingRecord.MaxGpuPercent.HasValue
                            ? Math.Max(existingRecord.MaxGpuPercent.Value, gpuValues.Max())
                            : gpuValues.Max();
                    }

                    _logger.LogDebug("Updated hourly system stats for {Hour} with {Count} additional samples", hour, samples.Count);
                }
            }

            await db.SaveChangesAsync().ConfigureAwait(false);

            lock (_aggregationLock)
            {
                _lastHourlyAggregation = currentHour;
            }

            _logger.LogInformation("Aggregated {Count} hourly system stats records", hourlyGroups.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to aggregate hourly system stats");
        }
    }

    /// <inheritdoc />
    public async Task AggregateDailyAsync()
    {
        if (_disposed)
            return;

        var today = DateOnly.FromDateTime(DateTime.Now);
        var yesterday = today.AddDays(-1);

        // Only aggregate if we haven't done so for yesterday
        lock (_aggregationLock)
        {
            if (_lastDailyAggregation >= yesterday.ToDateTime(TimeOnly.MinValue))
                return;
        }

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

            // Get hourly stats for yesterday
            var startOfYesterday = yesterday.ToDateTime(TimeOnly.MinValue);
            var endOfYesterday = yesterday.ToDateTime(new TimeOnly(23, 59, 59));

            var hourlyStats = await db.HourlySystemStats
                .Where(h => h.Hour >= startOfYesterday && h.Hour <= endOfYesterday)
                .ToListAsync()
                .ConfigureAwait(false);

            if (hourlyStats.Count == 0)
            {
                _logger.LogDebug("No hourly stats found for {Date}, skipping daily aggregation", yesterday);
                lock (_aggregationLock)
                {
                    _lastDailyAggregation = yesterday.ToDateTime(TimeOnly.MinValue);
                }
                return;
            }

            // Check if daily record already exists
            var existingDaily = await db.DailySystemStats
                .FirstOrDefaultAsync(d => d.Date == yesterday)
                .ConfigureAwait(false);

            if (existingDaily != null)
            {
                _logger.LogDebug("Daily stats for {Date} already exist, skipping", yesterday);
                lock (_aggregationLock)
                {
                    _lastDailyAggregation = yesterday.ToDateTime(TimeOnly.MinValue);
                }
                return;
            }

            // Aggregate hourly stats into daily
            var dailyRecord = new DailySystemStats
            {
                Date = yesterday,
                AvgCpuPercent = hourlyStats.Average(h => h.AvgCpuPercent),
                MaxCpuPercent = hourlyStats.Max(h => h.MaxCpuPercent),
                AvgMemoryPercent = hourlyStats.Average(h => h.AvgMemoryPercent),
                MaxMemoryPercent = hourlyStats.Max(h => h.MaxMemoryPercent),
                PeakMemoryUsedBytes = hourlyStats.Max(h => h.AvgMemoryUsedBytes)
            };

            // Handle GPU stats if any hourly records have them
            var hourlyWithGpu = hourlyStats.Where(h => h.AvgGpuPercent.HasValue).ToList();
            if (hourlyWithGpu.Count > 0)
            {
                dailyRecord.AvgGpuPercent = hourlyWithGpu.Average(h => h.AvgGpuPercent!.Value);
                dailyRecord.MaxGpuPercent = hourlyWithGpu.Where(h => h.MaxGpuPercent.HasValue).Max(h => h.MaxGpuPercent);
            }

            db.DailySystemStats.Add(dailyRecord);
            await db.SaveChangesAsync().ConfigureAwait(false);

            lock (_aggregationLock)
            {
                _lastDailyAggregation = yesterday.ToDateTime(TimeOnly.MinValue);
            }

            _logger.LogInformation("Created daily system stats for {Date} from {Count} hourly records", yesterday, hourlyStats.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to aggregate daily system stats");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Don't block with GetAwaiter().GetResult() - can cause deadlocks in UI contexts.
        // If synchronous disposal is called, we skip the final aggregation.
        // Use DisposeAsync for proper async cleanup.
        _logger.LogDebug("Synchronous Dispose called - final aggregation will be skipped. Use DisposeAsync for proper cleanup.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Final aggregation attempt on disposal
        try
        {
            await AggregateHourlyAsync().ConfigureAwait(false);
            _logger.LogDebug("Final aggregation completed during async disposal");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to perform final aggregation during async disposal");
        }
    }
}
