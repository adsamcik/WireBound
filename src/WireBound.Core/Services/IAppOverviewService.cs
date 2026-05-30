using System.ComponentModel;
using System.Runtime.CompilerServices;
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
    int HoursActive) : INotifyPropertyChanged
{
    public long TotalBytes => BytesReceived + BytesSent;
    public string FormattedBytesReceived => Helpers.ByteFormatter.FormatBytes(BytesReceived);
    public string FormattedBytesSent => Helpers.ByteFormatter.FormatBytes(BytesSent);
    public string FormattedTotalBytes => Helpers.ByteFormatter.FormatBytes(TotalBytes);
    public string FormattedAvgPrivateBytes => Helpers.ByteFormatter.FormatBytes(AvgPrivateBytes);
    public string FormattedPeakPrivateBytes => Helpers.ByteFormatter.FormatBytes(PeakPrivateBytes);

    /// <summary>
    /// Local file path to a cached PNG of the executable's icon, or null when
    /// no icon could be extracted (platform without support, missing binary,
    /// or transient extraction failure). UI binds to this with a generic
    /// placeholder rendered when null.
    /// </summary>
    public string? IconPath { get; init; }

    /// <summary>True when an icon path has been resolved for this app.</summary>
    public bool HasIcon => !string.IsNullOrEmpty(IconPath);

    // ─────────────────────────────────────────────────────────────────────
    // LIVE CPU SAMPLING
    //
    // Updated in-place on a timer by AppsViewModel from
    // IResourceInsightsService.GetRollingCpuByApp. Lives here (rather than
    // on a sibling VM) so the UI can bind directly without an intermediate
    // wrapper. NaN = "no live sample yet"; UI shows a placeholder.
    // ─────────────────────────────────────────────────────────────────────

    private double _liveCpuPercent = double.NaN;

    /// <summary>
    /// Rolling-60-second average CPU% for this app, or <c>NaN</c> when no
    /// live sample has arrived yet. Mutated by the Apps tab's live timer;
    /// raises INPC so existing rows update without rebuilding the list.
    /// </summary>
    public double LiveCpuPercent
    {
        get => _liveCpuPercent;
        set
        {
            // Guard against pointless notifications when the value barely
            // changed — saves a lot of repaint churn at the visual layer.
            if (double.IsNaN(value) && double.IsNaN(_liveCpuPercent))
                return;
            if (!double.IsNaN(value) && !double.IsNaN(_liveCpuPercent) &&
                Math.Abs(_liveCpuPercent - value) < 0.05)
                return;
            _liveCpuPercent = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasLiveCpu));
            OnPropertyChanged(nameof(FormattedLiveCpu));
        }
    }

    /// <summary>True when a live CPU sample is available; false = show placeholder.</summary>
    public bool HasLiveCpu => !double.IsNaN(_liveCpuPercent);

    /// <summary>"4.2%" when sample exists, "—" otherwise.</summary>
    public string FormattedLiveCpu => HasLiveCpu ? $"{_liveCpuPercent:F1}%" : "—";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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
