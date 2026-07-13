using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using WireBound.Avalonia.Helpers;
using WireBound.Core;
using WireBound.Core.Helpers;
using WireBound.Core.Services;
using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Avalonia.ViewModels;

/// <summary>
/// Filters the connection list by traffic scope.
/// </summary>
public enum ConnectionScope
{
    /// <summary>All connections.</summary>
    All,
    /// <summary>External (non-loopback) connections only.</summary>
    Network,
    /// <summary>Loopback / localhost connections only.</summary>
    Local
}

/// <summary>
/// Display model for a network connection with formatted properties
/// </summary>
public partial class ConnectionDisplayItem : ObservableObject
{
    [ObservableProperty]
    private string _protocol = "TCP";

    [ObservableProperty]
    private string _localEndpoint = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRemoteEndpoint))]
    private string _remoteEndpoint = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRemoteEndpoint))]
    private string _remoteHostname = "";

    /// <summary>
    /// True when the remote address is a loopback address (127.0.0.0/8 or ::1),
    /// in which case <see cref="DisplayName"/> is shown as "localhost".
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRemoteEndpoint))]
    [NotifyPropertyChangedFor(nameof(ScopeLabel))]
    private bool _isLoopback;

    [ObservableProperty]
    private string _displayName = "";

    /// <summary>
    /// Whether to show the raw endpoint subtitle beneath the remote title — shown
    /// when a hostname was resolved, or for loopback (so the IP stays visible under
    /// the "localhost" label).
    /// </summary>
    public bool ShowRemoteEndpoint => IsLoopback || !string.IsNullOrEmpty(RemoteHostname);

    /// <summary>
    /// Short label distinguishing loopback ("Local") from external ("Network") traffic.
    /// </summary>
    public string ScopeLabel => IsLoopback ? "Local" : "Network";

    [ObservableProperty]
    private string _processName = "";

    [ObservableProperty]
    private int _processId;

    [ObservableProperty]
    private string _state = "";

    [ObservableProperty]
    private string _bytesSent = "0 B";

    [ObservableProperty]
    private string _bytesReceived = "0 B";

    [ObservableProperty]
    private string _sendSpeed = "0 B/s";

    [ObservableProperty]
    private string _receiveSpeed = "0 B/s";

    [ObservableProperty]
    private bool _hasByteCounters;

    public long ReceiveSpeedBps { get; private set; }
    public long SendSpeedBps { get; private set; }
    public long BytesSentValue { get; private set; }
    public long BytesReceivedValue { get; private set; }

    public static ConnectionDisplayItem FromConnectionStats(ConnectionStats stats)
    {
        return new ConnectionDisplayItem
        {
            Protocol = stats.Protocol,
            LocalEndpoint = $"{stats.LocalAddress}:{stats.LocalPort}",
            RemoteEndpoint = stats.RemoteEndpoint,
            RemoteHostname = stats.ResolvedHostname ?? "",
            IsLoopback = IsLoopbackAddress(stats.RemoteAddress),
            DisplayName = FormatRemoteTitle(stats),
            ProcessName = stats.ProcessName,
            ProcessId = stats.ProcessId,
            State = stats.State.ToString(),
            BytesSent = ByteFormatter.FormatBytes(stats.BytesSent),
            BytesReceived = ByteFormatter.FormatBytes(stats.BytesReceived),
            SendSpeed = ByteFormatter.FormatSpeed(stats.SendSpeedBps),
            ReceiveSpeed = ByteFormatter.FormatSpeed(stats.ReceiveSpeedBps),
            BytesSentValue = stats.BytesSent,
            BytesReceivedValue = stats.BytesReceived,
            SendSpeedBps = stats.SendSpeedBps,
            ReceiveSpeedBps = stats.ReceiveSpeedBps,
            HasByteCounters = stats.HasByteCounters
        };
    }

    /// <summary>
    /// Produces the primary remote label: a resolved hostname if available,
    /// "localhost" for loopback addresses, otherwise the raw IP.
    /// </summary>
    private static string FormatRemoteTitle(ConnectionStats stats)
    {
        if (!string.IsNullOrEmpty(stats.ResolvedHostname))
            return stats.ResolvedHostname;
        return IsLoopbackAddress(stats.RemoteAddress) ? "localhost" : stats.RemoteAddress;
    }

    private static bool IsLoopbackAddress(string address) =>
        System.Net.IPAddress.TryParse(address, out var ip) && System.Net.IPAddress.IsLoopback(ip);

    public void UpdateFrom(ConnectionStats stats)
    {
        var newState = stats.State.ToString();
        if (State != newState)
            State = newState;

        // Process attribution can change after creation (e.g. a pre-existing
        // connection gets attributed from the OS table on a later refresh).
        if (ProcessName != stats.ProcessName)
            ProcessName = stats.ProcessName;
        if (ProcessId != stats.ProcessId)
            ProcessId = stats.ProcessId;

        if (BytesSentValue != stats.BytesSent)
        {
            BytesSentValue = stats.BytesSent;
            BytesSent = ByteFormatter.FormatBytes(stats.BytesSent);
        }

        if (BytesReceivedValue != stats.BytesReceived)
        {
            BytesReceivedValue = stats.BytesReceived;
            BytesReceived = ByteFormatter.FormatBytes(stats.BytesReceived);
        }

        if (SendSpeedBps != stats.SendSpeedBps)
        {
            SendSpeedBps = stats.SendSpeedBps;
            SendSpeed = ByteFormatter.FormatSpeed(stats.SendSpeedBps);
        }

        if (ReceiveSpeedBps != stats.ReceiveSpeedBps)
        {
            ReceiveSpeedBps = stats.ReceiveSpeedBps;
            ReceiveSpeed = ByteFormatter.FormatSpeed(stats.ReceiveSpeedBps);
        }

        if (HasByteCounters != stats.HasByteCounters)
            HasByteCounters = stats.HasByteCounters;

        if (!string.IsNullOrEmpty(stats.ResolvedHostname))
        {
            if (RemoteHostname != stats.ResolvedHostname)
                RemoteHostname = stats.ResolvedHostname;
            if (DisplayName != stats.DisplayName)
                DisplayName = stats.DisplayName;
        }
    }
}

/// <summary>
/// ViewModel for the Connections page - displays active network connections by IP/URL.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Byte Tracking Limitation:</strong> Per-connection byte counters (BytesSent, BytesReceived, speeds)
/// require the elevated helper process to provide accurate values. Without the helper:
/// </para>
/// <list type="bullet">
///   <item>Connection list, process info, and states work fully</item>
///   <item>Byte counters show estimated values based on proportional traffic distribution</item>
///   <item>The <see cref="IsByteTrackingLimited"/> property indicates when estimates are in use</item>
/// </list>
/// <para>
/// This limitation exists because per-socket byte accounting requires elevated access:
/// ETW on Windows, eBPF on Linux. See docs/LIMITATIONS.md for details.
/// </para>
/// </remarks>
public sealed partial class ConnectionsViewModel : ObservableObject, IDisposable
{
    private readonly IUiDispatcher _dispatcher;
    private readonly IProcessNetworkService? _processNetworkService;
    private readonly IDnsResolverService? _dnsResolver;
    private readonly IElevationService _elevationService;
    private readonly INavigationService _navigationService;
    private readonly IClipboardService _clipboardService;
    private readonly ILogger<ConnectionsViewModel>? _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, ConnectionDisplayItem> _connectionMap = new();
    private readonly HashSet<string> _displayedKeys = new();
    private ITimer? _refreshTimer;
    private bool _disposed;
    private bool _isViewActive;

    /// <summary>Completes when async initialization finishes. Exposed for testability.</summary>
    public Task InitializationTask { get; }

    /// <summary>Exposes the last search-triggered refresh task for testability.</summary>
    internal Task? PendingSearchTask { get; private set; }

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// True only until the first connection refresh completes. The full-screen
    /// loading overlay binds to this so periodic refreshes update the list in place
    /// instead of flashing the overlay on every tick.
    /// </summary>
    [ObservableProperty]
    private bool _isInitialLoading = true;

    [ObservableProperty]
    private bool _isPlatformSupported = true;

    [ObservableProperty]
    private bool _isMonitoring;

    [ObservableProperty]
    private bool _requiresElevation;

    /// <summary>
    /// Indicates that per-connection byte tracking is using estimated values
    /// because the elevated helper process is not connected.
    /// </summary>
    [ObservableProperty]
    private bool _isByteTrackingLimited;

    /// <summary>
    /// True while a <see cref="StartElevatedHelperAsync"/> request is in
    /// flight (covers the UAC/pkexec dialog window). Bound to the button's
    /// enable state so the user cannot fire a second request mid-handshake.
    /// </summary>
    [ObservableProperty]
    private bool _isStartingHelper;

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private int _connectionCount;

    [ObservableProperty]
    private int _tcpCount;

    [ObservableProperty]
    private int _udpCount;

    [ObservableProperty]
    private string _totalSent = "0 B";

    [ObservableProperty]
    private string _totalReceived = "0 B";

    // Split totals: localhost (loopback) vs external network traffic.
    [ObservableProperty]
    private string _networkReceived = "0 B";

    [ObservableProperty]
    private string _networkSent = "0 B";

    [ObservableProperty]
    private string _localReceived = "0 B";

    [ObservableProperty]
    private string _localSent = "0 B";

    /// <summary>
    /// Scope filter applied to the connection list (All / Network / Local). Does not
    /// affect the summary cards, which always reflect the full picture.
    /// </summary>
    [ObservableProperty]
    private ConnectionScope _scopeFilter = ConnectionScope.All;

    /// <summary>
    /// True when at least one connection is currently shown (after filtering). The
    /// "no connections" empty state binds to the inverse of this.
    /// </summary>
    [ObservableProperty]
    private bool _hasVisibleConnections;

    [ObservableProperty]
    private BatchObservableCollection<ConnectionDisplayItem> _connections = new();

    [ObservableProperty]
    private ConnectionDisplayItem? _selectedConnection;

    [ObservableProperty]
    private string _sortColumn = "Speed";

    [ObservableProperty]
    private bool _sortAscending;

    public ConnectionsViewModel(
        IUiDispatcher dispatcher,
        IProcessNetworkService processNetworkService,
        IDnsResolverService dnsResolver,
        IElevationService elevationService,
        INavigationService navigationService,
        IClipboardService clipboardService,
        ILogger<ConnectionsViewModel>? logger = null,
        TimeProvider? timeProvider = null)
    {
        _dispatcher = dispatcher;
        _processNetworkService = processNetworkService;
        _dnsResolver = dnsResolver;
        _elevationService = elevationService;
        _navigationService = navigationService;
        _clipboardService = clipboardService;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _isViewActive = navigationService.CurrentView == Routes.Connections;

        IsPlatformSupported = _processNetworkService?.IsPlatformSupported ?? false;
        IsMonitoring = _processNetworkService?.IsRunning == true;
        RequiresElevation = _elevationService.RequiresElevationFor(ElevatedFeature.PerProcessNetworkMonitoring)
                            && _elevationService.IsElevationSupported;

        // Byte tracking is limited when elevated helper is not connected
        IsByteTrackingLimited = !_elevationService.IsHelperConnected;

        // Set up refresh timer (2 seconds, initially stopped)
        _refreshTimer = _timeProvider.CreateTimer(
            async _ => await RefreshConnectionsAsync(),
            null,
            Timeout.InfiniteTimeSpan,
            TimeSpan.FromMilliseconds(2000));

        if (_processNetworkService != null)
        {
            _processNetworkService.StatsUpdated += OnProcessStatsUpdated;
            _processNetworkService.ErrorOccurred += OnProcessErrorOccurred;
        }

        if (_dnsResolver != null)
        {
            _dnsResolver.HostnameResolved += OnHostnameResolved;
        }

        // Subscribe to helper state changes
        _elevationService.HelperConnectionStateChanged += OnHelperConnectionStateChanged;

        // Subscribe to navigation changes for timer management
        _navigationService.NavigationChanged += OnNavigationChanged;

        InitializationTask = InitializeAsync();
    }

    private void OnHelperConnectionStateChanged(object? sender, HelperConnectionStateChangedEventArgs e)
    {
        _dispatcher.Post(() =>
        {
            IsByteTrackingLimited = !e.IsConnected;
            RequiresElevation = !e.IsConnected
                                && _elevationService.RequiresElevationFor(ElevatedFeature.PerProcessNetworkMonitoring)
                                && _elevationService.IsElevationSupported;
        }, UiDispatcherPriority.Background);
    }

    /// <summary>
    /// Starts the minimal elevated helper process from the Connections page
    /// banner. Same pattern as the Settings "Start Helper" button — this does
    /// NOT elevate the main app; it launches a separate, locked-down helper
    /// that only exposes ETW/netlink telemetry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// User-initiated, so <c>allowInteractive: true</c> is implicit (default).
    /// On Windows this triggers a one-time UAC prompt unless the helper is
    /// already registered with Task Scheduler. On Linux this triggers pkexec
    /// unless the systemd template is installed and the polkit policy is
    /// active.
    /// </para>
    /// <para>
    /// The command is bound to <see cref="IsByteTrackingLimited"/> so it
    /// disappears the moment the helper connects and updates the banner.
    /// </para>
    /// </remarks>
    [RelayCommand]
    private async Task StartElevatedHelperAsync()
    {
        if (!_elevationService.IsElevationSupported)
        {
            _logger?.LogWarning("Helper start requested but elevation not supported on this platform");
            return;
        }

        if (_elevationService.IsHelperConnected)
        {
            _logger?.LogDebug("Helper already connected");
            return;
        }

        IsStartingHelper = true;
        try
        {
            _logger?.LogInformation("User requested to start elevated helper from Connections page");
            var result = await _elevationService.StartHelperAsync();

            if (result.IsSuccess)
            {
                _logger?.LogInformation("Helper process started successfully");
                IsByteTrackingLimited = !_elevationService.IsHelperConnected;
                RequiresElevation = _elevationService.RequiresElevationFor(ElevatedFeature.PerProcessNetworkMonitoring)
                                    && !_elevationService.IsHelperConnected;
            }
            else if (result.Status == ElevationStatus.Cancelled)
            {
                _logger?.LogInformation("User cancelled helper elevation request");
            }
            else
            {
                _logger?.LogWarning("Failed to start helper: {Error}", result.ErrorMessage);
                HasError = true;
                ErrorMessage = result.ErrorMessage ?? "Failed to start elevated helper";
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unexpected error starting elevated helper");
            HasError = true;
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsStartingHelper = false;
        }
    }

    private void OnNavigationChanged(string route)
    {
        var wasActive = _isViewActive;
        _isViewActive = route == Routes.Connections;

        if (_isViewActive && !wasActive)
        {
            _refreshTimer?.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(2000));
            _ = RefreshConnectionsAsync();
        }
        else if (!_isViewActive && wasActive)
        {
            _refreshTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    private async Task InitializeAsync()
    {
        if (_disposed) return;

        if (_processNetworkService != null)
        {
            var started = await _processNetworkService.StartAsync();
            if (_disposed) return;
            IsMonitoring = started;
        }

        if (_disposed) return;
        // Only start timer if view is currently active
        if (_isViewActive)
        {
            _refreshTimer?.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(2000));
            await RefreshConnectionsAsync();
        }
    }

    private void OnProcessStatsUpdated(object? sender, ProcessStatsUpdatedEventArgs e)
    {
        // Process stats updated - we'll refresh connections on our timer
    }

    private void OnProcessErrorOccurred(object? sender, ProcessNetworkErrorEventArgs e)
    {
        _dispatcher.Post(() =>
        {
            HasError = true;
            ErrorMessage = e.Message;
            RequiresElevation = e.RequiresElevation;
        }, UiDispatcherPriority.Background);
    }

    private void OnHostnameResolved(object? sender, DnsResolvedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Hostname)) return;

        _dispatcher.Post(() =>
        {
            // Update all known connections with this IP — including hidden ones, so a
            // newly resolved hostname can reveal a search match — then re-evaluate
            // their visibility against the current filter.
            foreach (var kvp in _connectionMap)
            {
                var conn = kvp.Value;
                if (conn.RemoteEndpoint.StartsWith(e.IpAddress))
                {
                    conn.RemoteHostname = e.Hostname;
                    conn.DisplayName = e.Hostname;
                    UpdateItemVisibility(kvp.Key);
                }
            }
            HasVisibleConnections = Connections.Count > 0;
        }, UiDispatcherPriority.Background);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await RefreshConnectionsAsync();
    }

    private async Task RefreshConnectionsAsync()
    {
        if (_disposed) return;
        if (_processNetworkService == null) return;

        try
        {
            await _dispatcher.InvokeAsync(() =>
            {
                HasError = false;
            });

            // Get connection stats from the service
            var stats = await _processNetworkService.GetConnectionStatsAsync();

            await _dispatcher.InvokeAsync(() =>
            {
                UpdateConnectionsList(stats);
                IsLoading = false;
                IsInitialLoading = false;
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to refresh connections");
            await _dispatcher.InvokeAsync(() =>
            {
                HasError = true;
                ErrorMessage = $"Failed to refresh connections: {ex.Message}";
                IsLoading = false;
                IsInitialLoading = false;
            });
        }
    }

    private void UpdateConnectionsList(IReadOnlyList<ConnectionStats> stats)
    {
        var currentKeys = new HashSet<string>();
        long totalSent = 0, totalReceived = 0;
        long networkSent = 0, networkReceived = 0;
        long localSent = 0, localReceived = 0;
        int tcpCount = 0;
        int udpCount = 0;

        foreach (var stat in stats)
        {
            var key = stat.ConnectionKey;
            currentKeys.Add(key);

            if (stat.Protocol == "TCP") tcpCount++;
            else udpCount++;

            // Queue DNS resolution for remote address
            _dnsResolver?.QueueForResolution(stat.RemoteAddress);

            // Try to get cached hostname
            var hostname = _dnsResolver?.GetCached(stat.RemoteAddress);
            if (hostname != null)
            {
                stat.ResolvedHostname = hostname;
            }

            if (!_connectionMap.TryGetValue(key, out var item))
            {
                item = ConnectionDisplayItem.FromConnectionStats(stat);
                _connectionMap[key] = item;
            }
            else
            {
                item.UpdateFrom(stat);
            }

            // Split totals by loopback vs external.
            totalSent += stat.BytesSent;
            totalReceived += stat.BytesReceived;
            if (item.IsLoopback)
            {
                localSent += stat.BytesSent;
                localReceived += stat.BytesReceived;
            }
            else
            {
                networkSent += stat.BytesSent;
                networkReceived += stat.BytesReceived;
            }

            UpdateItemVisibility(key);
        }

        // Remove stale connections from the master map and the displayed list.
        foreach (var key in _connectionMap.Keys.Where(k => !currentKeys.Contains(k)).ToList())
        {
            if (_connectionMap.Remove(key, out var item))
            {
                if (_displayedKeys.Remove(key))
                    Connections.Remove(item);
            }
        }

        // Summary cards reflect the full picture (independent of the list filter).
        ConnectionCount = _connectionMap.Count;
        TcpCount = tcpCount;
        UdpCount = udpCount;
        TotalSent = ByteFormatter.FormatBytes(totalSent);
        TotalReceived = ByteFormatter.FormatBytes(totalReceived);
        NetworkSent = ByteFormatter.FormatBytes(networkSent);
        NetworkReceived = ByteFormatter.FormatBytes(networkReceived);
        LocalSent = ByteFormatter.FormatBytes(localSent);
        LocalReceived = ByteFormatter.FormatBytes(localReceived);
        HasVisibleConnections = Connections.Count > 0;
    }

    /// <summary>
    /// Whether a connection passes the current scope filter and search text.
    /// </summary>
    private bool IsVisible(ConnectionDisplayItem item)
    {
        if (ScopeFilter == ConnectionScope.Local && !item.IsLoopback) return false;
        if (ScopeFilter == ConnectionScope.Network && item.IsLoopback) return false;

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var s = SearchText;
            return item.RemoteEndpoint.Contains(s, StringComparison.OrdinalIgnoreCase)
                || item.RemoteHostname.Contains(s, StringComparison.OrdinalIgnoreCase)
                || item.ProcessName.Contains(s, StringComparison.OrdinalIgnoreCase)
                || item.DisplayName.Contains(s, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    /// <summary>
    /// Incrementally adds or removes a single connection from the displayed list to
    /// match the current filter — avoids a full collection reset (and the resulting
    /// flicker) on every refresh.
    /// </summary>
    private void UpdateItemVisibility(string key)
    {
        if (!_connectionMap.TryGetValue(key, out var item)) return;

        var visible = IsVisible(item);
        var displayed = _displayedKeys.Contains(key);

        if (visible && !displayed)
        {
            Connections.Add(item);
            _displayedKeys.Add(key);
        }
        else if (!visible && displayed)
        {
            Connections.Remove(item);
            _displayedKeys.Remove(key);
        }
    }

    /// <summary>
    /// Re-evaluates the visibility of every known connection against the current
    /// filter, incrementally. Used when the scope filter or search text changes.
    /// </summary>
    private void ReapplyFilter()
    {
        foreach (var key in _connectionMap.Keys.ToList())
            UpdateItemVisibility(key);

        HasVisibleConnections = Connections.Count > 0;
    }

    partial void OnSearchTextChanged(string value)
    {
        // Filter locally right away for responsiveness, then refresh from the service
        // (the periodic refresh also re-applies the predicate).
        ReapplyFilter();
        PendingSearchTask = RefreshConnectionsAsync();
    }

    partial void OnScopeFilterChanged(ConnectionScope value) => ReapplyFilter();

    [RelayCommand]
    private void SelectScope(ConnectionScope scope) => ScopeFilter = scope;

    [RelayCommand]
    private void SortBy(string column)
    {
        if (SortColumn == column)
        {
            SortAscending = !SortAscending;
        }
        else
        {
            SortColumn = column;
            SortAscending = false;
        }

        SortConnections();
    }

    private void SortConnections()
    {
        var sorted = SortColumn switch
        {
            "Protocol" => SortAscending
                ? Connections.OrderBy(c => c.Protocol)
                : Connections.OrderByDescending(c => c.Protocol),
            "Remote" => SortAscending
                ? Connections.OrderBy(c => c.DisplayName)
                : Connections.OrderByDescending(c => c.DisplayName),
            "Process" => SortAscending
                ? Connections.OrderBy(c => c.ProcessName)
                : Connections.OrderByDescending(c => c.ProcessName),
            "State" => SortAscending
                ? Connections.OrderBy(c => c.State)
                : Connections.OrderByDescending(c => c.State),
            _ => SortAscending
                ? Connections.OrderBy(c => c.ReceiveSpeedBps)
                : Connections.OrderByDescending(c => c.ReceiveSpeedBps)
        };

        Connections.ReplaceAll(sorted);
    }

    [RelayCommand]
    private async Task CopyToClipboardAsync(string text)
    {
        await _clipboardService.SetTextAsync(text);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _refreshTimer?.Dispose();

        if (_processNetworkService != null)
        {
            _processNetworkService.StatsUpdated -= OnProcessStatsUpdated;
            _processNetworkService.ErrorOccurred -= OnProcessErrorOccurred;
        }

        if (_dnsResolver != null)
        {
            _dnsResolver.HostnameResolved -= OnHostnameResolved;
        }

        _elevationService.HelperConnectionStateChanged -= OnHelperConnectionStateChanged;
        _navigationService.NavigationChanged -= OnNavigationChanged;
    }
}
