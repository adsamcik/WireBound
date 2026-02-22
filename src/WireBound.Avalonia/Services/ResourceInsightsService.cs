using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WireBound.Core.Data;
using WireBound.Core.Models;
using WireBound.Core.Services;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Avalonia.Services;

/// <summary>
/// Combines IProcessResourceProvider + IAppCategoryService to produce smoothed,
/// grouped resource insights with historical persistence.
/// Handles CPU% calculation from time deltas and dual-rate EMA smoothing.
/// </summary>
public sealed class ResourceInsightsService : IResourceInsightsService
{
    private readonly IProcessResourceProvider _provider;
    private readonly IAppCategoryService _categoryService;
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ResourceInsightsService>? _logger;

    private readonly int _processorCount;

    // Per-app smoothing state (keyed by app identifier)
    private readonly ConcurrentDictionary<string, AppSmoothingState> _smoothingStates = new();

    // Previous snapshot for CPU% delta calculation
    private IReadOnlyList<ProcessSnapshotEntry>? _previousSnapshot;
    private DateTime _previousSnapshotTime;

    // EMA tuning constants
    private const double MemoryAlphaUp = 0.3;
    private const double MemoryAlphaDown = 0.1;
    private const double CpuAlphaUp = 0.4;
    private const double CpuAlphaDown = 0.15;

    public ResourceInsightsService(
        IProcessResourceProvider provider,
        IAppCategoryService categoryService,
        IServiceProvider serviceProvider,
        TimeProvider? timeProvider = null,
        ILogger<ResourceInsightsService>? logger = null)
    {
        _provider = provider;
        _categoryService = categoryService;
        _serviceProvider = serviceProvider;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
        _processorCount = Environment.ProcessorCount;
    }

    public async Task<IReadOnlyList<AppResourceUsage>> GetCurrentByAppAsync(
        CancellationToken cancellationToken = default)
    {
        var rawData = await _provider.GetProcessResourceDataAsync(cancellationToken);
        var now = _timeProvider.GetLocalNow().DateTime;
        var grouped = GroupByApp(rawData);
        var withCpu = ComputeCpuPercent(grouped, now);
        var smoothed = ApplySmoothing(withCpu);

        return smoothed
            .OrderByDescending(a => a.PrivateBytes)
            .ToList();
    }

    public Task<IReadOnlyList<CategoryResourceUsage>> GetCurrentByCategoryAsync(
        CancellationToken cancellationToken = default)
    {
        // NOTE: Do NOT call GetCurrentByAppAsync here — it re-polls all processes
        // and corrupts the CPU delta state. Callers should pass the app list instead.
        // This method is kept for interface compatibility; prefer the overload below.
        throw new InvalidOperationException(
            "Use GetCurrentByCategoryAsync(apps) overload to avoid re-polling processes.");
    }

    /// <summary>
    /// Groups pre-fetched app data into categories without re-polling processes.
    /// </summary>
    public IReadOnlyList<CategoryResourceUsage> GetCategoryBreakdown(
        IReadOnlyList<AppResourceUsage> apps)
    {
        return GroupByCategory(apps);
    }

    public async Task<IReadOnlyList<ResourceInsightSnapshot>> GetHistoricalByAppAsync(
        DateOnly start, DateOnly end, CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();
        var startDt = start.ToDateTime(TimeOnly.MinValue);
        var endDt = end.ToDateTime(TimeOnly.MaxValue);

        return await context.ResourceInsightSnapshots
            .Where(s => s.Timestamp >= startDt && s.Timestamp <= endDt)
            .OrderBy(s => s.Timestamp)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ResourceInsightSnapshot>> GetHistoricalByCategoryAsync(
        DateOnly start, DateOnly end, CancellationToken cancellationToken = default)
    {
        var snapshots = await GetHistoricalByAppAsync(start, end, cancellationToken);

        // Aggregate by category + timestamp
        return snapshots
            .GroupBy(s => new { s.CategoryName, s.Timestamp, s.Granularity })
            .Select(g => new ResourceInsightSnapshot
            {
                Timestamp = g.Key.Timestamp,
                CategoryName = g.Key.CategoryName,
                AppIdentifier = g.Key.CategoryName,
                AppName = g.Key.CategoryName,
                PrivateBytes = (long)g.Average(s => s.PrivateBytes),
                WorkingSetBytes = (long)g.Average(s => s.WorkingSetBytes),
                CpuPercent = g.Sum(s => s.CpuPercent),
                PeakPrivateBytes = g.Max(s => s.PeakPrivateBytes),
                PeakCpuPercent = g.Max(s => s.PeakCpuPercent),
                Granularity = g.Key.Granularity,
                LastUpdated = g.Max(s => s.LastUpdated)
            })
            .OrderBy(s => s.Timestamp)
            .ThenBy(s => s.CategoryName)
            .ToList();
    }

    public async Task RecordSnapshotAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var apps = await GetCurrentByAppAsync(cancellationToken);
            if (apps.Count == 0) return;

            var now = _timeProvider.GetLocalNow().DateTime;
            var hourTimestamp = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);

            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

            // Batch-load all existing snapshots for this hour to avoid N+1 queries
            var appIds = apps
                .Where(a => !string.IsNullOrEmpty(a.AppIdentifier))
                .Select(a => a.AppIdentifier)
                .Distinct()
                .ToList();
            var existingSnapshots = await context.ResourceInsightSnapshots
                .Where(s =>
                    s.Timestamp == hourTimestamp &&
                    appIds.Contains(s.AppIdentifier) &&
                    s.Granularity == UsageGranularity.Hourly)
                .ToDictionaryAsync(s => s.AppIdentifier, cancellationToken)
                .ConfigureAwait(false);

            foreach (var app in apps)
            {
                if (string.IsNullOrEmpty(app.AppIdentifier)) continue;

                existingSnapshots.TryGetValue(app.AppIdentifier, out var existing);

                if (existing != null)
                {
                    // Update running averages using incremental mean
                    existing.PrivateBytes = (existing.PrivateBytes + app.PrivateBytes) / 2;
                    existing.CpuPercent = (existing.CpuPercent + app.CpuPercent) / 2;
                    existing.PeakPrivateBytes = Math.Max(existing.PeakPrivateBytes, app.PrivateBytes);
                    existing.PeakCpuPercent = Math.Max(existing.PeakCpuPercent, app.CpuPercent);
                    existing.LastUpdated = now;
                }
                else
                {
                    context.ResourceInsightSnapshots.Add(new ResourceInsightSnapshot
                    {
                        Timestamp = hourTimestamp,
                        AppIdentifier = app.AppIdentifier,
                        AppName = app.AppName,
                        CategoryName = app.CategoryName,
                        PrivateBytes = app.PrivateBytes,
                        WorkingSetBytes = 0,
                        CpuPercent = app.CpuPercent,
                        PeakPrivateBytes = app.PrivateBytes,
                        PeakCpuPercent = app.CpuPercent,
                        Granularity = UsageGranularity.Hourly,
                        LastUpdated = now
                    });
                }
            }

            await context.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to record resource insight snapshot");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // GROUPING
    // ═══════════════════════════════════════════════════════════════════════════

    private List<AppRawData> GroupByApp(
        IReadOnlyList<Platform.Abstract.Models.ProcessResourceData> rawData)
    {
        return rawData
            .GroupBy(p => GetAppKey(p.ProcessName, p.ExecutablePath))
            .Select(g =>
            {
                var first = g.First();
                var exePath = g.FirstOrDefault(p => !string.IsNullOrEmpty(p.ExecutablePath))?.ExecutablePath
                              ?? string.Empty;
                return new AppRawData
                {
                    AppIdentifier = ComputeAppIdentifier(exePath, first.ProcessName),
                    AppName = GetDisplayName(first.ProcessName, exePath),
                    ExecutablePath = exePath,
                    ProcessName = first.ProcessName,
                    TotalPrivateBytes = g.Sum(p => p.PrivateBytes),
                    TotalCpuTimeTicks = g.Sum(p => p.CpuTimeTicks),
                    ProcessCount = g.Count()
                };
            })
            .ToList();
    }

    private List<AppWithCpu> ComputeCpuPercent(List<AppRawData> current, DateTime now)
    {
        var result = new List<AppWithCpu>(current.Count);
        var prevLookup = BuildPreviousLookup();
        var wallTimeTicks = _previousSnapshot != null
            ? (now - _previousSnapshotTime).Ticks
            : 0L;

        foreach (var app in current)
        {
            double cpuPercent = 0;
            if (wallTimeTicks > 0 && prevLookup.TryGetValue(app.AppIdentifier, out var prevTicks))
            {
                var cpuDelta = app.TotalCpuTimeTicks - prevTicks;
                if (cpuDelta > 0)
                {
                    cpuPercent = (double)cpuDelta / wallTimeTicks / _processorCount * 100;
                    cpuPercent = Math.Clamp(cpuPercent, 0, 100.0 * _processorCount);
                }
            }

            result.Add(new AppWithCpu
            {
                AppIdentifier = app.AppIdentifier,
                AppName = app.AppName,
                ExecutablePath = app.ExecutablePath,
                PrivateBytes = app.TotalPrivateBytes,
                CpuPercent = cpuPercent,
                ProcessCount = app.ProcessCount
            });
        }

        // Store current as previous for next delta
        _previousSnapshot = current.Select(a => new ProcessSnapshotEntry
        {
            AppIdentifier = a.AppIdentifier,
            CpuTimeTicks = a.TotalCpuTimeTicks
        }).ToList();
        _previousSnapshotTime = now;

        return result;
    }

    private Dictionary<string, long> BuildPreviousLookup()
    {
        if (_previousSnapshot == null)
            return new Dictionary<string, long>();

        var lookup = new Dictionary<string, long>(_previousSnapshot.Count);
        foreach (var entry in _previousSnapshot)
        {
            lookup[entry.AppIdentifier] = entry.CpuTimeTicks;
        }
        return lookup;
    }

    private List<AppResourceUsage> ApplySmoothing(List<AppWithCpu> apps)
    {
        var result = new List<AppResourceUsage>(apps.Count);

        foreach (var app in apps)
        {
            var state = _smoothingStates.GetOrAdd(app.AppIdentifier, _ => new AppSmoothingState());

            var smoothedMemory = state.SmoothMemory(app.PrivateBytes);
            var smoothedCpu = state.SmoothCpu(app.CpuPercent);

            result.Add(new AppResourceUsage
            {
                AppIdentifier = app.AppIdentifier,
                AppName = app.AppName,
                ExecutablePath = app.ExecutablePath,
                CategoryName = _categoryService.GetCategory(app.ExecutablePath.Length > 0
                    ? Path.GetFileNameWithoutExtension(app.ExecutablePath)
                    : app.AppName),
                PrivateBytes = smoothedMemory,
                CpuPercent = smoothedCpu,
                ProcessCount = app.ProcessCount
            });
        }

        return result;
    }

    private static List<CategoryResourceUsage> GroupByCategory(IReadOnlyList<AppResourceUsage> apps)
    {
        return apps
            .GroupBy(a => a.CategoryName)
            .Select(g => new CategoryResourceUsage
            {
                CategoryName = g.Key,
                TotalPrivateBytes = g.Sum(a => a.PrivateBytes),
                TotalCpuPercent = g.Sum(a => a.CpuPercent),
                AppCount = g.Count(),
                ProcessCount = g.Sum(a => a.ProcessCount)
            })
            .OrderByDescending(c => c.TotalPrivateBytes)
            .ToList();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════════════════════

    private static string GetAppKey(string processName, string exePath)
    {
        // Group by exe path when available, fallback to process name
        return !string.IsNullOrEmpty(exePath)
            ? exePath.ToLowerInvariant()
            : processName.ToLowerInvariant();
    }

    private static string ComputeAppIdentifier(string exePath, string processName)
    {
        var source = !string.IsNullOrEmpty(exePath) ? exePath : processName;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(source.ToLowerInvariant()));
        return Convert.ToHexString(hash)[..16];
    }

    private static string GetDisplayName(string processName, string exePath)
    {
        if (!string.IsNullOrEmpty(exePath))
        {
            var fileName = Path.GetFileNameWithoutExtension(exePath);
            if (!string.IsNullOrEmpty(fileName))
                return fileName;
        }
        return processName;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SMOOTHING STATE
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Per-app dual-rate EMA state for both memory and CPU.
    /// </summary>
    private sealed class AppSmoothingState
    {
        private double _smoothedMemory = -1;
        private double _smoothedCpu = -1;

        /// <summary>
        /// Apply dual-rate EMA to memory: fast up (α=0.3), slow down (α=0.1).
        /// </summary>
        public long SmoothMemory(long rawBytes)
        {
            if (_smoothedMemory < 0)
            {
                _smoothedMemory = rawBytes;
                return rawBytes;
            }

            var alpha = rawBytes > _smoothedMemory ? MemoryAlphaUp : MemoryAlphaDown;
            _smoothedMemory = _smoothedMemory * (1 - alpha) + rawBytes * alpha;
            return (long)_smoothedMemory;
        }

        /// <summary>
        /// Apply dual-rate EMA to CPU: fast up (α=0.4), slow down (α=0.15).
        /// </summary>
        public double SmoothCpu(double rawPercent)
        {
            if (_smoothedCpu < 0)
            {
                _smoothedCpu = rawPercent;
                return rawPercent;
            }

            var alpha = rawPercent > _smoothedCpu ? CpuAlphaUp : CpuAlphaDown;
            _smoothedCpu = _smoothedCpu * (1 - alpha) + rawPercent * alpha;
            return Math.Max(0, _smoothedCpu);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // INTERNAL DTOs
    // ═══════════════════════════════════════════════════════════════════════════

    private sealed class AppRawData
    {
        public string AppIdentifier { get; init; } = "";
        public string AppName { get; init; } = "";
        public string ExecutablePath { get; init; } = "";
        public string ProcessName { get; init; } = "";
        public long TotalPrivateBytes { get; init; }
        public long TotalCpuTimeTicks { get; init; }
        public int ProcessCount { get; init; }
    }

    private sealed class AppWithCpu
    {
        public string AppIdentifier { get; init; } = "";
        public string AppName { get; init; } = "";
        public string ExecutablePath { get; init; } = "";
        public long PrivateBytes { get; init; }
        public double CpuPercent { get; init; }
        public int ProcessCount { get; init; }
    }

    private sealed class ProcessSnapshotEntry
    {
        public string AppIdentifier { get; init; } = "";
        public long CpuTimeTicks { get; init; }
    }
}
