using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
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
/// Display model for a network connection with formatted properties
/// </summary>
public partial class ConnectionDisplayItem : ObservableObject
{
    [ObservableProperty]
    private string _protocol = "TCP";

    [ObservableProperty]
    private string _localEndpoint = "";

    [ObservableProperty]
    private string _remoteEndpoint = "";

    [ObservableProperty]
    private string _remoteHostname = "";

    [ObservableProperty]
    private string _displayName = "";

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

    public static ConnectionDisplayItem FromConnectionStats(ConnectionStats stats)
    {
        return new ConnectionDisplayItem
        {
            Protocol = stats.Protocol,
            LocalEndpoint = $"{stats.LocalAddress}:{stats.LocalPort}",
            RemoteEndpoint = stats.RemoteEndpoint,
            RemoteHostname = stats.ResolvedHostname ?? "",
            DisplayName = stats.DisplayName,
            ProcessName = stats.ProcessName,
            ProcessId = stats.ProcessId,
            State = stats.State.ToString(),
            BytesSent = ByteFormatter.FormatBytes(stats.BytesSent),
            BytesReceived = ByteFormatter.FormatBytes(stats.BytesReceived),
            SendSpeed = ByteFormatter.FormatSpeed(stats.SendSpeedBps),
            ReceiveSpeed = ByteFormatter.FormatSpeed(stats.ReceiveSpeedBps),
            HasByteCounters = stats.HasByteCounters
        };
    }

    public void UpdateFrom(ConnectionStats stats)
    {
        State = stats.State.ToString();
        BytesSent = ByteFormatter.FormatBytes(stats.BytesSent);
        BytesReceived = ByteFormatter.FormatBytes(stats.BytesReceived);
        SendSpeed = ByteFormatter.FormatSpeed(stats.SendSpeedBps);
        ReceiveSpeed = ByteFormatter.FormatSpeed(stats.ReceiveSpeedBps);
        HasByteCounters = stats.HasByteCounters;

        if (!string.IsNullOrEmpty(stats.ResolvedHostname))
        {
            RemoteHostname = stats.ResolvedHostname;
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
    private readonly ILogger<ConnectionsViewModel>? _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, ConnectionDisplayItem> _connectionMap = new();
    private ITimer? _refreshTimer;
    private bool _disposed;
    private bool _isViewActive;

    /// <summary>Completes when async initialization finishes. Exposed for testability.</summary>
    public Task InitializationTask { get; }

    /// <summary>Exposes the last search-triggered refresh task for testability.</summary>
    internal Task? PendingSearchTask { get; private set; }

    [ObservableProperty]
    private bool _isLoading;

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
        ILogger<ConnectionsViewModel>? logger = null,
        TimeProvider? timeProvider = null)
    {
        _dispatcher = dispatcher;
        _processNetworkService = processNetworkService;
        _dnsResolver = dnsResolver;
        _elevationService = elevationService;
        _navigationService = navigationService;
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
        }, UiDispatcherPriority.Background);
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
            // Update any connections with this IP
            foreach (var conn in Connections)
            {
                if (conn.RemoteEndpoint.StartsWith(e.IpAddress))
                {
                    conn.RemoteHostname = e.Hostname;
                    conn.DisplayName = e.Hostname;
                }
            }
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
            IsLoading = true;
            HasError = false;

            // Get connection stats from the service
            var stats = await _processNetworkService.GetConnectionStatsAsync();

            await _dispatcher.InvokeAsync(() =>
            {
                UpdateConnectionsList(stats);
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to refresh connections");
            _dispatcher.Post(() =>
            {
                HasError = true;
                ErrorMessage = $"Failed to refresh connections: {ex.Message}";
            }, UiDispatcherPriority.Background);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void UpdateConnectionsList(IReadOnlyList<ConnectionStats> stats)
    {
        var currentKeys = new HashSet<string>();
        long totalSent = 0;
        long totalReceived = 0;
        int tcpCount = 0;
        int udpCount = 0;

        foreach (var stat in stats)
        {
            var key = stat.ConnectionKey;
            currentKeys.Add(key);

            totalSent += stat.BytesSent;
            totalReceived += stat.BytesReceived;
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

            if (_connectionMap.TryGetValue(key, out var existing))
            {
                existing.UpdateFrom(stat);
            }
            else
            {
                var item = ConnectionDisplayItem.FromConnectionStats(stat);
                _connectionMap[key] = item;
                Connections.Add(item);
            }
        }

        // Remove stale connections
        var staleCount = 0;
        foreach (var key in _connectionMap.Keys.Where(k => !currentKeys.Contains(k)).ToList())
        {
            if (_connectionMap.Remove(key, out var item))
            {
                Connections.Remove(item);
                staleCount++;
            }
        }

        // Apply search filter
        ApplyFilter();

        // Update stats
        ConnectionCount = Connections.Count;
        TcpCount = tcpCount;
        UdpCount = udpCount;
        TotalSent = ByteFormatter.FormatBytes(totalSent);
        TotalReceived = ByteFormatter.FormatBytes(totalReceived);
    }

    private void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
            return;

        var search = SearchText;
        // Build filtered list and replace in one notification
        var filtered = new List<ConnectionDisplayItem>(Connections.Count);
        foreach (var c in Connections)
        {
            if (c.RemoteEndpoint.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                c.RemoteHostname.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                c.ProcessName.Contains(search, StringComparison.OrdinalIgnoreCase))
            {
                filtered.Add(c);
            }
        }

        if (filtered.Count < Connections.Count)
            Connections.ReplaceAll(filtered);
    }

    partial void OnSearchTextChanged(string value)
    {
        PendingSearchTask = RefreshConnectionsAsync();
    }

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
                ? Connections.OrderBy(c => c.ReceiveSpeed)
                : Connections.OrderByDescending(c => c.ReceiveSpeed)
        };

        Connections.ReplaceAll(sorted);
    }

    [RelayCommand]
    private async Task CopyToClipboardAsync(string text)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var clipboard = desktop.MainWindow?.Clipboard;
            if (clipboard != null)
                await clipboard.SetTextAsync(text);
        }
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
