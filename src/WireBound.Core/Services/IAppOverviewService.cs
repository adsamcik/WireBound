using Helpers = WireBound.Core.Helpers;

namespace WireBound.Core.Services;

/// <summary>
/// Read-only service that combines per-application network and resource history
/// into shapes for the unified Apps tab.
/// </summary>
public interface IAppOverviewService
{
    /// <summary>
    /// Returns one row per AppIdentifier seen in <paramref name="start"/>..<paramref name="end"/>
    /// (inclusive of both dates, using DateTime.Now-local semantics like the
    /// rest of the codebase), with bytes summed and CPU/RAM averaged/peaked
    /// across all hour buckets in the range.
    /// </summary>
    Task<IReadOnlyList<AppOverview>> GetOverviewAsync(
        DateOnly start, DateOnly end, CancellationToken ct = default);

    /// <summary>
    /// Hour-bucket network bytes (down + up) for a single app over the range.
    /// Used by the detail drawer's network history chart. Empty list when
    /// the app has no rows in <c>AppUsageRecords</c> for the range.
    /// </summary>
    Task<IReadOnlyList<AppNetworkHistoryPoint>> GetNetworkHistoryAsync(
        string appIdentifier, DateOnly start, DateOnly end, CancellationToken ct = default);

    /// <summary>
    /// Hour-bucket CPU% + RAM (PrivateBytes) for a single app over the range.
    /// Used by the detail drawer's resource history charts. Empty list when
    /// the app fell below the low-activity persistence threshold OR has no
    /// resource snapshots at all (the helper might not be running).
    /// </summary>
    Task<IReadOnlyList<AppResourceHistoryPoint>> GetResourceHistoryAsync(
        string appIdentifier, DateOnly start, DateOnly end, CancellationToken ct = default);

    /// <summary>
    /// Top remote endpoints (by total bytes) seen anywhere in the range.
    /// NOT filtered by AppIdentifier — the AddressUsageRecords table records
    /// per-endpoint stats without app attribution. Detail drawer uses this
    /// as a "network destinations during this period" overview.
    /// </summary>
    Task<IReadOnlyList<TopDestinationEntry>> GetTopDestinationsAsync(
        int limit, DateOnly start, DateOnly end, CancellationToken ct = default);
}

/// <summary>
/// Aggregated application row for the unified Apps master list.
/// <c>CategoryName</c> comes only from <c>ResourceInsightSnapshots</c> and is
/// empty when no resource snapshot exists for the application in the range.
/// </summary>
public sealed record AppOverview(
    string AppIdentifier,
    string AppName,
    string ProcessName,
    string ExecutablePath,
    string CategoryName,
    long BytesReceived,
    long BytesSent,
    long PeakDownloadSpeed,
    long PeakUploadSpeed,
    double AvgCpuPercent,
    double MaxCpuPercent,
    long AvgPrivateBytes,
    long PeakPrivateBytes,
    DateTime FirstSeen,
    DateTime LastSeen,
    int HoursActive)
{
    public long TotalBytes => BytesReceived + BytesSent;
    public string FormattedBytesReceived => Helpers.ByteFormatter.FormatBytes(BytesReceived);
    public string FormattedBytesSent => Helpers.ByteFormatter.FormatBytes(BytesSent);
    public string FormattedTotalBytes => Helpers.ByteFormatter.FormatBytes(TotalBytes);
    public string FormattedAvgPrivateBytes => Helpers.ByteFormatter.FormatBytes(AvgPrivateBytes);
    public string FormattedPeakPrivateBytes => Helpers.ByteFormatter.FormatBytes(PeakPrivateBytes);
}

/// <summary>
/// Network byte totals for one application in one hourly bucket.
/// </summary>
public sealed record AppNetworkHistoryPoint(DateTime Timestamp, long BytesReceived, long BytesSent);

/// <summary>
/// Resource snapshot values for one application in one hourly bucket.
/// </summary>
public sealed record AppResourceHistoryPoint(DateTime Timestamp, double CpuPercent, long PrivateBytes);

/// <summary>
/// Top destination endpoint over a date range. This is not app-attributed:
/// <c>AddressUsageRecords</c> stores endpoint totals without an AppIdentifier.
/// </summary>
public sealed record TopDestinationEntry(
    string RemoteAddress,
    string? Hostname,
    int Port,
    string Protocol,
    long BytesSent,
    long BytesReceived)
{
    public long TotalBytes => BytesSent + BytesReceived;
}
