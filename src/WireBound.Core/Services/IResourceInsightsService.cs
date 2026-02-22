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
    /// and return grouped-by-application results.
    /// </summary>
    Task<IReadOnlyList<AppResourceUsage>> GetCurrentByAppAsync(
        CancellationToken cancellationToken = default);

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
    /// Record a snapshot of current resource usage to the database.
    /// Called periodically by the background polling service.
    /// </summary>
    Task RecordSnapshotAsync(CancellationToken cancellationToken = default);
}
