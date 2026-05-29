using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WireBound.Core.Data;
using WireBound.Core.Models;
using WireBound.Core.Services;
using WireBound.Platform.Abstract.Helpers;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Avalonia.Services;

/// <summary>
/// Read-only Apps-tab data plumbing over persisted app network, resource,
/// and endpoint history.
/// </summary>
public sealed class AppOverviewService : IAppOverviewService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IAppIconService _iconService;

    public AppOverviewService(IServiceProvider serviceProvider, IAppIconService iconService)
    {
        _serviceProvider = serviceProvider;
        _iconService = iconService;
    }

    public async Task<IReadOnlyList<AppOverview>> GetOverviewAsync(
        DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        if (!TryGetRange(start, end, out var startDate, out var endDate))
        {
            return [];
        }

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var networkRows = await db.AppUsageRecords
            .AsNoTracking()
            .Where(r =>
                r.Granularity == UsageGranularity.Hourly &&
                r.Timestamp >= startDate &&
                r.Timestamp <= endDate &&
                r.AppIdentifier != string.Empty &&
                r.AppIdentifier != AppIdentity.UnknownIdentifier)
            .Select(r => new NetworkRow(
                r.AppIdentifier,
                r.AppName,
                r.ProcessName,
                r.ExecutablePath,
                r.Timestamp,
                r.BytesReceived,
                r.BytesSent,
                r.PeakDownloadSpeed,
                r.PeakUploadSpeed,
                r.LastUpdated))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var resourceRows = await db.ResourceInsightSnapshots
            .AsNoTracking()
            .Where(r =>
                r.Granularity == UsageGranularity.Hourly &&
                r.Timestamp >= startDate &&
                r.Timestamp <= endDate &&
                r.AppIdentifier != string.Empty &&
                r.AppIdentifier != AppIdentity.UnknownIdentifier)
            .Select(r => new ResourceRow(
                r.AppIdentifier,
                r.AppName,
                r.CategoryName,
                r.Timestamp,
                r.PrivateBytes,
                r.CpuPercent,
                r.PeakPrivateBytes,
                r.PeakCpuPercent,
                r.LastUpdated))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // Normalize identifiers to lowercase before the in-memory join.
        // Legacy ResourceInsightSnapshots rows store uppercase hex (the
        // service used to call its own ComputeAppIdentifier without the
        // final ToLowerInvariant) while AppUsageRecords always stores
        // lowercase. A case-sensitive join would leave every app as two
        // phantom rows — one network-only with the AppName, one
        // resource-only with the CategoryName. Defensive lowercasing here
        // joins both legacy and current rows correctly.
        var networkByApp = networkRows
            .GroupBy(r => r.AppIdentifier.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var resourceByApp = resourceRows
            .GroupBy(r => r.AppIdentifier.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var appIdentifiers = networkByApp.Keys.Concat(resourceByApp.Keys).Distinct(StringComparer.Ordinal);

        var overview = new List<AppOverview>();
        foreach (var appIdentifier in appIdentifiers)
        {
            networkByApp.TryGetValue(appIdentifier, out var appNetworkRows);
            resourceByApp.TryGetValue(appIdentifier, out var appResourceRows);
            appNetworkRows ??= [];
            appResourceRows ??= [];

            var timestamps = appNetworkRows.Select(r => r.Timestamp)
                .Concat(appResourceRows.Select(r => r.Timestamp))
                .ToList();

            if (timestamps.Count == 0)
            {
                continue;
            }

            overview.Add(new AppOverview(
                appIdentifier,
                PickLatestNonEmpty(
                    appNetworkRows.Select(r => new TextCandidate(r.AppName, r.LastUpdated))
                        .Concat(appResourceRows.Select(r => new TextCandidate(r.AppName, r.LastUpdated)))),
                PickLatestNonEmpty(appNetworkRows.Select(r => new TextCandidate(r.ProcessName, r.LastUpdated))),
                PickLatestNonEmpty(appNetworkRows.Select(r => new TextCandidate(r.ExecutablePath, r.LastUpdated))),
                PickLatestNonEmpty(appResourceRows.Select(r => new TextCandidate(r.CategoryName, r.LastUpdated))),
                appNetworkRows.Sum(r => r.BytesReceived),
                appNetworkRows.Sum(r => r.BytesSent),
                appNetworkRows.Count == 0 ? 0 : appNetworkRows.Max(r => r.PeakDownloadSpeed),
                appNetworkRows.Count == 0 ? 0 : appNetworkRows.Max(r => r.PeakUploadSpeed),
                appResourceRows.Count == 0 ? 0 : appResourceRows.Average(r => r.CpuPercent),
                appResourceRows.Count == 0 ? 0 : appResourceRows.Max(r => r.PeakCpuPercent),
                appResourceRows.Count == 0 ? 0 : Convert.ToInt64(appResourceRows.Average(r => r.PrivateBytes)),
                appResourceRows.Count == 0 ? 0 : appResourceRows.Max(r => r.PeakPrivateBytes),
                timestamps.Min(),
                timestamps.Max(),
                timestamps.Select(ToHourBucket).Distinct().Count()));
        }

        var sorted = overview
            .OrderByDescending(r => r.TotalBytes)
            .ThenBy(r => r.AppName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.AppIdentifier, StringComparer.Ordinal)
            .ToList();

        // Resolve icons in parallel after the heavy aggregation finishes so
        // we don't block the list render on disk I/O. Each call is cheap
        // (cache hit after first extraction), and IAppIconService swallows
        // failures internally — we treat null as "no icon, show placeholder".
        var iconTasks = sorted
            .Select(async (app, index) =>
            {
                if (string.IsNullOrEmpty(app.ExecutablePath))
                {
                    return (index, path: (string?)null);
                }
                var path = await _iconService
                    .GetIconPathAsync(app.ExecutablePath, app.AppIdentifier, ct)
                    .ConfigureAwait(false);
                return (index, path);
            })
            .ToList();

        var resolved = await Task.WhenAll(iconTasks).ConfigureAwait(false);
        foreach (var (index, path) in resolved)
        {
            if (path is not null)
            {
                sorted[index] = sorted[index] with { IconPath = path };
            }
        }

        return sorted;
    }

    public async Task<IReadOnlyList<AppNetworkHistoryPoint>> GetNetworkHistoryAsync(
        string appIdentifier, DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        if (!IsTrackableAppIdentifier(appIdentifier) ||
            !TryGetRange(start, end, out var startDate, out var endDate))
        {
            return [];
        }

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        return await db.AppUsageRecords
            .AsNoTracking()
            .Where(r =>
                r.AppIdentifier == appIdentifier &&
                r.Granularity == UsageGranularity.Hourly &&
                r.Timestamp >= startDate &&
                r.Timestamp <= endDate)
            .OrderBy(r => r.Timestamp)
            .Select(r => new AppNetworkHistoryPoint(r.Timestamp, r.BytesReceived, r.BytesSent))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AppResourceHistoryPoint>> GetResourceHistoryAsync(
        string appIdentifier, DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        if (!IsTrackableAppIdentifier(appIdentifier) ||
            !TryGetRange(start, end, out var startDate, out var endDate))
        {
            return [];
        }

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        // Compare case-insensitively because legacy ResourceInsightSnapshots
        // rows store the identifier in uppercase hex (see ResourceInsightsService
        // history). EF Core translates EF.Functions.Like over a column to a
        // SQLite COLLATE NOCASE comparison, which catches both cases without
        // requiring a one-off data migration.
        var loweredId = appIdentifier.ToLowerInvariant();
        return await db.ResourceInsightSnapshots
            .AsNoTracking()
            .Where(r =>
                r.AppIdentifier.ToLower() == loweredId &&
                r.Granularity == UsageGranularity.Hourly &&
                r.Timestamp >= startDate &&
                r.Timestamp <= endDate)
            .OrderBy(r => r.Timestamp)
            .Select(r => new AppResourceHistoryPoint(r.Timestamp, r.CpuPercent, r.PrivateBytes))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TopDestinationEntry>> GetTopDestinationsAsync(
        int limit, DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        if (limit <= 0 || !TryGetRange(start, end, out var startDate, out var endDate))
        {
            return [];
        }

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();

        var rows = await db.AddressUsageRecords
            .AsNoTracking()
            .Where(r =>
                r.Granularity == UsageGranularity.Hourly &&
                r.Timestamp >= startDate &&
                r.Timestamp <= endDate)
            .Select(r => new DestinationRow(
                r.RemoteAddress,
                r.Hostname,
                r.PrimaryPort,
                r.Protocol,
                r.BytesSent,
                r.BytesReceived,
                r.LastUpdated))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows
            .GroupBy(r => new { r.RemoteAddress, r.Port, r.Protocol })
            .Select(g => new TopDestinationEntry(
                g.Key.RemoteAddress,
                PickLatestNonEmpty(g.Select(r => new TextCandidate(r.Hostname, r.LastUpdated))) is { Length: > 0 } hostname
                    ? hostname
                    : null,
                g.Key.Port,
                g.Key.Protocol,
                g.Sum(r => r.BytesSent),
                g.Sum(r => r.BytesReceived)))
            .OrderByDescending(r => r.TotalBytes)
            .ThenBy(r => r.RemoteAddress, StringComparer.Ordinal)
            .ThenBy(r => r.Port)
            .Take(limit)
            .ToList();
    }

    private static bool TryGetRange(DateOnly start, DateOnly end, out DateTime startDate, out DateTime endDate)
    {
        if (end < start)
        {
            startDate = default;
            endDate = default;
            return false;
        }

        startDate = start.ToDateTime(TimeOnly.MinValue);
        endDate = end.ToDateTime(TimeOnly.MaxValue);
        return true;
    }

    private static bool IsTrackableAppIdentifier(string appIdentifier) =>
        !string.IsNullOrEmpty(appIdentifier) &&
        !string.Equals(appIdentifier, AppIdentity.UnknownIdentifier, StringComparison.Ordinal);

    private static string PickLatestNonEmpty(IEnumerable<TextCandidate> candidates) =>
        candidates
            .Where(c => !string.IsNullOrEmpty(c.Value))
            .OrderByDescending(c => c.LastUpdated)
            .Select(c => c.Value!)
            .FirstOrDefault() ?? string.Empty;

    private static DateTime ToHourBucket(DateTime timestamp) =>
        new(timestamp.Year, timestamp.Month, timestamp.Day, timestamp.Hour, 0, 0, timestamp.Kind);

    private sealed record NetworkRow(
        string AppIdentifier,
        string AppName,
        string ProcessName,
        string ExecutablePath,
        DateTime Timestamp,
        long BytesReceived,
        long BytesSent,
        long PeakDownloadSpeed,
        long PeakUploadSpeed,
        DateTime LastUpdated);

    private sealed record ResourceRow(
        string AppIdentifier,
        string AppName,
        string CategoryName,
        DateTime Timestamp,
        long PrivateBytes,
        double CpuPercent,
        long PeakPrivateBytes,
        double PeakCpuPercent,
        DateTime LastUpdated);

    private sealed record DestinationRow(
        string RemoteAddress,
        string? Hostname,
        int Port,
        string Protocol,
        long BytesSent,
        long BytesReceived,
        DateTime LastUpdated);

    private sealed record TextCandidate(string? Value, DateTime LastUpdated);
}
