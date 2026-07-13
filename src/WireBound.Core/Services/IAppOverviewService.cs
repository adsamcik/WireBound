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

    // ─────────────────────────────────────────────────────────────────────
    // LOOPBACK / NETWORK SPLIT
    //
    // Loopback bytes are the subset of this app's traffic that went to
    // localhost (127.0.0.0/8, ::1) — e.g. adb tunnels, dev servers, local
    // IPC. They're populated only for data captured with the elevated helper;
    // older/estimated data reads as zero loopback (i.e. all network).
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Loopback (localhost) bytes received over the period.</summary>
    public long LoopbackBytesReceived { get; init; }

    /// <summary>Loopback (localhost) bytes sent over the period.</summary>
    public long LoopbackBytesSent { get; init; }

    /// <summary>Network (non-loopback) bytes received.</summary>
    public long NetworkBytesReceived => System.Math.Max(0, BytesReceived - LoopbackBytesReceived);

    /// <summary>Network (non-loopback) bytes sent.</summary>
    public long NetworkBytesSent => System.Math.Max(0, BytesSent - LoopbackBytesSent);

    /// <summary>Network (non-loopback) total bytes — the default headline figure.</summary>
    public long NetworkTotalBytes => NetworkBytesReceived + NetworkBytesSent;

    /// <summary>Loopback (localhost) total bytes.</summary>
    public long LoopbackTotalBytes => System.Math.Min(TotalBytes, LoopbackBytesReceived + LoopbackBytesSent);

    /// <summary>True when this app has any measured loopback traffic.</summary>
    public bool HasLoopbackTraffic => LoopbackBytesReceived + LoopbackBytesSent > 0;

    public string FormattedNetworkBytesReceived => Helpers.ByteFormatter.FormatBytes(NetworkBytesReceived);
    public string FormattedNetworkBytesSent => Helpers.ByteFormatter.FormatBytes(NetworkBytesSent);
    public string FormattedNetworkTotalBytes => Helpers.ByteFormatter.FormatBytes(NetworkTotalBytes);
    public string FormattedLocalTotalBytes => Helpers.ByteFormatter.FormatBytes(LoopbackTotalBytes);

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
            OnPropertyChanged(nameof(IsCurrentlyRunning));
        }
    }

    /// <summary>True when a live CPU sample is available; false = show placeholder.</summary>
    public bool HasLiveCpu => !double.IsNaN(_liveCpuPercent);

    /// <summary>"4.2%" when sample exists, "—" otherwise.</summary>
    public string FormattedLiveCpu => HasLiveCpu ? $"{_liveCpuPercent:F1}%" : "—";

    /// <summary>
    /// True when the period-average CPU is large enough to be worth showing
    /// as the muted second line under the live value. When zero (idle app /
    /// no history) the avg line is just noise — the UI hides it.
    /// </summary>
    public bool HasMeaningfulAvgCpu => AvgCpuPercent >= 0.05;

    private double _liveRamBytes = double.NaN;

    /// <summary>
    /// Rolling-60-second average private-bytes RAM for this app, or
    /// <c>NaN</c> when no live sample has arrived yet. Same lifecycle as
    /// <see cref="LiveCpuPercent"/> — mutated by the Apps tab live timer.
    /// </summary>
    public double LiveRamBytes
    {
        get => _liveRamBytes;
        set
        {
            if (double.IsNaN(value) && double.IsNaN(_liveRamBytes))
                return;
            // 1 MB tolerance: RAM samples wobble in the low kilobytes constantly,
            // pointless to repaint for every tiny variation.
            if (!double.IsNaN(value) && !double.IsNaN(_liveRamBytes) &&
                Math.Abs(_liveRamBytes - value) < 1_000_000)
                return;
            _liveRamBytes = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasLiveRam));
            OnPropertyChanged(nameof(FormattedLiveRam));
            OnPropertyChanged(nameof(IsCurrentlyRunning));
        }
    }

    /// <summary>True when a live RAM sample is available; false = show placeholder.</summary>
    public bool HasLiveRam => !double.IsNaN(_liveRamBytes);

    /// <summary>"256 MB" when sample exists, "—" otherwise.</summary>
    public string FormattedLiveRam => HasLiveRam ? Helpers.ByteFormatter.FormatBytes((long)_liveRamBytes) : "—";

    /// <summary>
    /// True when the period-average RAM is large enough to be worth showing
    /// under the live value. Threshold is 1 MB — anything below that is
    /// effectively idle and the muted "avg X" line adds no information.
    /// </summary>
    public bool HasMeaningfulAvgRam => AvgPrivateBytes >= 1_000_000;

    /// <summary>
    /// True when at least one live CPU or RAM sample has arrived in the last
    /// 60 seconds — a reliable proxy for "the process is alive on this
    /// machine right now". Apps that exited (or never started during the
    /// session) flip to false within the rolling window. Used by the Apps
    /// list to render an "Idle" badge and dim non-running rows so the user
    /// can scan running vs historical entries at a glance.
    /// </summary>
    public bool IsCurrentlyRunning => HasLiveCpu || HasLiveRam;

    // ─────────────────────────────────────────────────────────────────────
    // GROUPING
    //
    // Apps related by install directory + similar name (e.g. "bridge" and
    // "bridge-gui", or "vlc" and "vlc-cache") are clustered into a single
    // expandable row by the Apps view. The grouping pass sets these
    // properties on a SYNTHESIZED head AppOverview (with aggregated stats)
    // and on each original child. Solo apps leave all of them at their
    // defaults so the UI treats them as ungrouped.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Stable identifier shared by every member of the same group. Null /
    /// empty when this app is not part of any group.
    /// </summary>
    public string? GroupKey { get; init; }

    /// <summary>
    /// True only on the synthesized head row for a group. The head shows
    /// aggregated stats (sum of member bytes, max of member peaks, etc.)
    /// and is the row the chevron toggles to reveal members.
    /// </summary>
    public bool IsGroupHead { get; init; }

    /// <summary>
    /// True on individual member rows. Members render indented underneath
    /// their head and are only included in the visible list when the head's
    /// <see cref="IsExpanded"/> is true.
    /// </summary>
    public bool IsGroupMember { get; init; }

    /// <summary>
    /// Number of constituent apps the head represents (including itself
    /// conceptually — i.e. an "(N)" badge value rendered next to the
    /// group's display name).
    /// </summary>
    public int GroupMemberCount { get; init; }

    private bool _isExpanded;

    /// <summary>
    /// User-toggled expansion state for a group head. Mutating raises
    /// INotifyPropertyChanged so the chevron rotates and so the VM can
    /// rebuild the visible list without rebuilding every row.
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged();
        }
    }

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
