using WireBound.Core.Models;

namespace WireBound.Core.Services;

/// <summary>
/// Service for per-process resource insights: smoothing, grouping by app/category,
/// CPU% calculation from time deltas, snapshot persistence, and historical queries.
/// </summary>
public interface IResourceInsightsService
{
    /// <summary>
    /// Poll current process resources, compute smoothed CPU% and memory,
    /// and return grouped-by-application results. Side effect: each call
    /// appends to per-app rolling buffers feeding
    /// <see cref="GetRollingCpuByApp(TimeSpan)"/>.
    /// </summary>
    Task<IReadOnlyList<AppResourceUsage>> GetCurrentByAppAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the rolling-average CPU% per app over the requested window
    /// (capped at the buffer's underlying retention, currently 2 min).
    /// Samples are added by <see cref="GetCurrentByAppAsync"/>; callers do
    /// NOT trigger a fresh poll here, so this is cheap. Missing keys mean
    /// "no live samples in window" — fall back to historical averages.
    /// </summary>
    IReadOnlyDictionary<string, double> GetRollingCpuByApp(TimeSpan window);

    /// <summary>
    /// Returns the rolling-average private-bytes RAM per app over the
    /// requested window. Same semantics as <see cref="GetRollingCpuByApp"/>:
    /// samples are fed by <see cref="GetCurrentByAppAsync"/>, missing keys
    /// mean "no live samples in window" — fall back to historical averages.
    /// </summary>
    IReadOnlyDictionary<string, double> GetRollingRamByApp(TimeSpan window);

    /// <summary>
    /// Group pre-fetched app data into category-level results without re-polling processes.
    /// </summary>
    IReadOnlyList<CategoryResourceUsage> GetCategoryBreakdown(
        IReadOnlyList<AppResourceUsage> apps);

    /// <summary>
    /// Poll current process resources, compute smoothed values,
    /// and return grouped-by-category results.
    /// WARNING: Re-polls all processes — prefer GetCategoryBreakdown(apps) when you already have app data.
    /// </summary>
    Task<IReadOnlyList<CategoryResourceUsage>> GetCurrentByCategoryAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get historical resource snapshots for a date range, grouped by app.
    /// </summary>
    Task<IReadOnlyList<ResourceInsightSnapshot>> GetHistoricalByAppAsync(
        DateOnly start, DateOnly end, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get historical resource snapshots for a date range, grouped by category.
    /// Returns snapshots aggregated by category name.
    /// </summary>
    Task<IReadOnlyList<ResourceInsightSnapshot>> GetHistoricalByCategoryAsync(
        DateOnly start, DateOnly end, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the current per-app resource snapshot to <see cref="ResourceInsightSnapshot"/>.
    /// Apps whose average CPU% is below 0.1 AND working set is below 50 MB
    /// are skipped so the table doesn't accumulate millions of rows for
    /// idle background services — they can still be seen live in the current
    /// snapshot endpoints, just not in historical aggregates.
    /// </summary>
    Task RecordSnapshotAsync(CancellationToken cancellationToken = default);
}
