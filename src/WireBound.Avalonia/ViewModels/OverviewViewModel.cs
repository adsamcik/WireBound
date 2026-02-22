using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using WireBound.Avalonia.Helpers;
using WireBound.Core;
using WireBound.Core.Helpers;
using WireBound.Core.Models;
using WireBound.Core.Services;

namespace WireBound.Avalonia.ViewModels;

/// <summary>
/// Time range options for the overview chart
/// </summary>
public enum TimeRange
{
    OneMinute,
    FiveMinutes,
    FifteenMinutes,
    OneHour
}

/// <summary>
/// Unified ViewModel combining network monitoring and system metrics
/// for the overview dashboard experience.
/// </summary>
public sealed partial class OverviewViewModel : ObservableObject, IDisposable
{
    private readonly IUiDispatcher _dispatcher;
    private readonly INetworkMonitorService _networkMonitor;
    private readonly ISystemMonitorService _systemMonitor;
    private readonly INavigationService _navigationService;
    private readonly IDataPersistenceService? _dataPersistence;
    private readonly ILogger<OverviewViewModel>? _logger;
    private bool _disposed;
    private bool _isViewActive;

    // Chart data collections
    private readonly BatchObservableCollection<DateTimePoint> _downloadSpeedPoints = new();
    private readonly BatchObservableCollection<DateTimePoint> _uploadSpeedPoints = new();
    private readonly BatchObservableCollection<DateTimePoint> _cpuOverlayPoints = new();
    private readonly BatchObservableCollection<DateTimePoint> _memoryOverlayPoints = new();

    // Cached overlay series (created once, toggled via Add/Remove)
    private readonly LineSeries<DateTimePoint> _cpuOverlaySeries;
    private readonly LineSeries<DateTimePoint> _memoryOverlaySeries;

    // Tracked secondary adapter series for Add/Remove
    private readonly List<ISeries> _secondaryAdapterSeries = [];

    // Secondary adapter chart data (keyed by adapter ID)
    private readonly Dictionary<string, BatchObservableCollection<DateTimePoint>> _secondaryAdapterPoints = new();

    private readonly ChartDataManager _chartDataManager = new(maxBufferSize: 3600, maxDisplayPoints: 300);

    // Trend tracking using shared calculator (arrows style for overview)
    private readonly TrendIndicatorCalculator _downloadTrendCalculator = new(iconStyle: TrendIconStyle.Arrows);
    private readonly TrendIndicatorCalculator _uploadTrendCalculator = new(iconStyle: TrendIconStyle.Arrows);

    // Today's stored bytes (from database at startup)
    private long _todayStoredReceived;
    private long _todayStoredSent;

    // Auto adapter tracking
    private string _lastResolvedPrimaryAdapterId = string.Empty;
    private CancellationTokenSource? _notificationCts;

    // Latest-wins coalescing: only one UI post in-flight at a time per event type
    private NetworkStats? _pendingNetworkStats;
    private int _networkUpdateQueued;
    private SystemStats? _pendingSystemStats;
    private int _systemUpdateQueued;

    // Cached formatted values to avoid string allocation when value unchanged
    private long _lastDownloadBps = -1;
    private long _lastUploadBps = -1;
    private long _lastSessionReceived = -1;
    private long _lastSessionSent = -1;
    private long _lastTodayReceived = -1;
    private long _lastTodaySent = -1;
    private long _lastPeakDownloadBps = -1;
    private long _lastPeakUploadBps = -1;
    private int _lastCpuPercentInt = -1;
    private int _lastMemPercentInt = -1;

    // Throttle secondary adapter discovery (every 5 ticks instead of every tick)
    private int _secondaryAdapterTickCounter;

    // Chart history limits
    private const int MaxHistoryPoints = 300;

    #region Network Properties

    [ObservableProperty]
    private string _downloadSpeed = "0 B/s";

    [ObservableProperty]
    private string _uploadSpeed = "0 B/s";

    [ObservableProperty]
    private string _peakDownloadSpeed = "0 B/s";

    [ObservableProperty]
    private string _peakUploadSpeed = "0 B/s";

    [ObservableProperty]
    private string _todayDownload = "0 B";

    [ObservableProperty]
    private string _todayUpload = "0 B";

    [ObservableProperty]
    private string _sessionDownload = "0 B";

    [ObservableProperty]
    private string _sessionUpload = "0 B";

    [ObservableProperty]
    private string _downloadTrendIcon = "";

    [ObservableProperty]
    private string _downloadTrendText = "stable";

    [ObservableProperty]
    private string _uploadTrendIcon = "";

    [ObservableProperty]
    private string _uploadTrendText = "stable";

    [ObservableProperty]
    private ObservableCollection<AdapterDisplayItem> _adapters = [];

    [ObservableProperty]
    private AdapterDisplayItem? _selectedAdapter;

    [ObservableProperty]
    private bool _showAdvancedAdapters;

    /// <summary>
    /// Secondary adapters with active traffic (VPN, other physical adapters not the primary)
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<SecondaryAdapterInfo> _secondaryAdapters = [];

    /// <summary>
    /// Whether there are any secondary adapters with active traffic
    /// </summary>
    [ObservableProperty]
    private bool _hasSecondaryAdapters;

    /// <summary>
    /// Notification text when auto adapter switches (empty = hidden)
    /// </summary>
    [ObservableProperty]
    private string _autoSwitchNotification = string.Empty;

    /// <summary>
    /// Whether the auto switch notification is visible
    /// </summary>
    [ObservableProperty]
    private bool _isAutoSwitchNotificationVisible;

    #endregion

    #region System Properties

    [ObservableProperty]
    private double _cpuPercent;

    [ObservableProperty]
    private double _memoryPercent;

    [ObservableProperty]
    private string _cpuUsageFormatted = "0%";

    [ObservableProperty]
    private string _memoryUsageFormatted = "0%";

    #endregion

    #region Chart Properties

    [ObservableProperty]
    private bool _showCpuOverlay;

    [ObservableProperty]
    private bool _showMemoryOverlay;

    [ObservableProperty]
    private TimeRange _selectedTimeRange = TimeRange.OneMinute;

    /// <summary>
    /// Main chart series (download/upload with optional CPU/Memory overlays)
    /// </summary>
    public ObservableCollection<ISeries> ChartSeries { get; } = [];

    /// <summary>
    /// X-axis configuration for the chart
    /// </summary>
    public Axis[] ChartXAxes { get; }

    /// <summary>
    /// Y-axis configuration for the chart (primary: speed, secondary: percentage)
    /// </summary>
    public Axis[] ChartYAxes { get; }

    /// <summary>
    /// Secondary Y-axis for percentage overlays (CPU/Memory)
    /// </summary>
    public Axis[] ChartSecondaryYAxes { get; }

    /// <summary>Completes when async initialization finishes. Exposed for testability.</summary>
    public Task InitializationTask { get; }

    /// <summary>
    /// Time range options for display in UI
    /// </summary>
    public static IReadOnlyList<TimeRangeDisplayItem> TimeRangeOptions { get; } =
    [
        new(TimeRange.OneMinute, "1m", "Last 1 minute", 60),
        new(TimeRange.FiveMinutes, "5m", "Last 5 minutes", 300),
        new(TimeRange.FifteenMinutes, "15m", "Last 15 minutes", 900),
        new(TimeRange.OneHour, "1h", "Last 1 hour", 3600)
    ];

    #endregion

    public OverviewViewModel(
        IUiDispatcher dispatcher,
        INetworkMonitorService networkMonitor,
        ISystemMonitorService systemMonitor,
        INavigationService navigationService,
        IDataPersistenceService? dataPersistence = null,
        ILogger<OverviewViewModel>? logger = null)
    {
        _dispatcher = dispatcher;
        _networkMonitor = networkMonitor;
        _systemMonitor = systemMonitor;
        _navigationService = navigationService;
        _dataPersistence = dataPersistence;
        _logger = logger;
        _isViewActive = navigationService.CurrentView == Routes.Overview;

        // Initialize chart axes
        ChartXAxes = ChartSeriesFactory.CreateTimeXAxes();
        ChartYAxes = ChartSeriesFactory.CreateSpeedYAxes();
        ChartSecondaryYAxes = ChartSeriesFactory.CreatePercentageYAxes();

        // Create cached overlay series (reused across toggles)
        _cpuOverlaySeries = ChartSeriesFactory.CreateOverlayLineSeries(
            "CPU", _cpuOverlayPoints, ChartColors.CpuColor, useDashedLine: true);
        _memoryOverlaySeries = ChartSeriesFactory.CreateOverlayLineSeries(
            "Memory", _memoryOverlayPoints, ChartColors.MemoryColor, useDashedLine: true);

        // Initialize chart series with base download/upload
        foreach (var series in CreateChartSeries())
            ChartSeries.Add(series);

        // Subscribe to network stats updates
        _networkMonitor.StatsUpdated += OnNetworkStatsUpdated;

        // Subscribe to system stats updates
        _systemMonitor.StatsUpdated += OnSystemStatsUpdated;

        // Subscribe to navigation changes for view-aware updates
        _navigationService.NavigationChanged += OnNavigationChanged;

        // Load adapters and restore saved selection
        LoadAdapters();

        // Load async initialization tasks
        InitializationTask = Task.WhenAll(RestoreSelectedAdapterAsync(), LoadTodayUsageAsync());

        // Get initial system stats
        var initialSystemStats = _systemMonitor.GetCurrentStats();
        UpdateSystemProperties(initialSystemStats);
    }

    #region Event Handlers

    private void OnNavigationChanged(string route)
    {
        var wasActive = _isViewActive;
        _isViewActive = route == Routes.Overview;

        if (_isViewActive && !wasActive)
        {
            // Refresh chart from buffer when becoming visible
            _dispatcher.Post(RefreshChartFromBuffer);
        }
    }

    private async void RefreshChartFromBuffer()
    {
        try
        {
            var rangeSeconds = GetTimeRangeSeconds(SelectedTimeRange);
            var (downloadPoints, uploadPoints) = await _chartDataManager.GetDisplayDataAsync(rangeSeconds);

            _downloadSpeedPoints.ReplaceAll(downloadPoints);
            _uploadSpeedPoints.ReplaceAll(uploadPoints);

            UpdatePeakSpeeds();

            if (ChartXAxes.Length > 0 && downloadPoints.Count > 0)
            {
                ChartXAxes[0].MinLimit = DateTime.Now.AddSeconds(-rangeSeconds).Ticks;
                ChartXAxes[0].MaxLimit = DateTime.Now.Ticks;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to refresh chart from buffer");
        }
    }

    private void OnNetworkStatsUpdated(object? sender, NetworkStats stats)
    {
        if (_disposed) return;

        Volatile.Write(ref _pendingNetworkStats, stats);
        if (Interlocked.Exchange(ref _networkUpdateQueued, 1) == 1) return;

        _dispatcher.Post(() =>
        {
            if (_disposed) return;
            var pending = _pendingNetworkStats;
            Interlocked.Exchange(ref _networkUpdateQueued, 0);
            if (pending != null) UpdateNetworkProperties(pending);
        }, UiDispatcherPriority.Background);
    }

    private void OnSystemStatsUpdated(object? sender, SystemStats stats)
    {
        if (_disposed) return;

        Volatile.Write(ref _pendingSystemStats, stats);
        if (Interlocked.Exchange(ref _systemUpdateQueued, 1) == 1) return;

        _dispatcher.Post(() =>
        {
            if (_disposed) return;
            var pending = _pendingSystemStats;
            Interlocked.Exchange(ref _systemUpdateQueued, 0);
            if (pending != null) UpdateSystemProperties(pending);
        }, UiDispatcherPriority.Background);
    }

    #endregion

    #region Property Updates

    private void UpdateNetworkProperties(NetworkStats stats)
    {
        var now = DateTime.Now;

        // Always buffer data (needed for chart history even when not visible)
        _chartDataManager.AddDataPoint(now, stats.DownloadSpeedBps, stats.UploadSpeedBps);

        // Skip UI-heavy updates when view is not active
        if (!_isViewActive) return;

        // Update speed displays (only format when value changed)
        if (_lastDownloadBps != stats.DownloadSpeedBps)
        {
            _lastDownloadBps = stats.DownloadSpeedBps;
            DownloadSpeed = ByteFormatter.FormatSpeed(stats.DownloadSpeedBps);
        }
        if (_lastUploadBps != stats.UploadSpeedBps)
        {
            _lastUploadBps = stats.UploadSpeedBps;
            UploadSpeed = ByteFormatter.FormatSpeed(stats.UploadSpeedBps);
        }

        // Update session totals
        if (_lastSessionReceived != stats.SessionBytesReceived)
        {
            _lastSessionReceived = stats.SessionBytesReceived;
            SessionDownload = ByteFormatter.FormatBytes(stats.SessionBytesReceived);
        }
        if (_lastSessionSent != stats.SessionBytesSent)
        {
            _lastSessionSent = stats.SessionBytesSent;
            SessionUpload = ByteFormatter.FormatBytes(stats.SessionBytesSent);
        }

        // Calculate today's totals (stored from previous sessions + current session)
        var todayReceivedTotal = _todayStoredReceived + stats.SessionBytesReceived;
        var todaySentTotal = _todayStoredSent + stats.SessionBytesSent;
        if (_lastTodayReceived != todayReceivedTotal)
        {
            _lastTodayReceived = todayReceivedTotal;
            TodayDownload = ByteFormatter.FormatBytes(todayReceivedTotal);
        }
        if (_lastTodaySent != todaySentTotal)
        {
            _lastTodaySent = todaySentTotal;
            TodayUpload = ByteFormatter.FormatBytes(todaySentTotal);
        }

        // Update trend indicators
        UpdateTrendIndicators(stats.DownloadSpeedBps, stats.UploadSpeedBps);

        // Update chart data
        UpdateChart(now, stats.DownloadSpeedBps, stats.UploadSpeedBps);

        // Update statistics
        UpdatePeakSpeeds();

        // Handle auto adapter features
        if (SelectedAdapter?.IsAuto == true)
        {
            // Update Auto display name with resolved adapter
            if (!string.IsNullOrEmpty(stats.ResolvedPrimaryAdapterName))
            {
                SelectedAdapter.UpdateAutoResolvedName(stats.ResolvedPrimaryAdapterName);
            }

            // Detect adapter switch and show notification
            if (!string.IsNullOrEmpty(stats.ResolvedPrimaryAdapterId) &&
                stats.ResolvedPrimaryAdapterId != _lastResolvedPrimaryAdapterId)
            {
                var previousId = _lastResolvedPrimaryAdapterId;
                _lastResolvedPrimaryAdapterId = stats.ResolvedPrimaryAdapterId;

                if (!string.IsNullOrEmpty(previousId))
                {
                    ShowAutoSwitchNotificationAsync(stats.ResolvedPrimaryAdapterName);
                }
            }
        }

        // Update secondary adapters on a throttled cadence (every 5 ticks ≈ 5s)
        _secondaryAdapterTickCounter++;
        if (_secondaryAdapterTickCounter >= 5)
        {
            _secondaryAdapterTickCounter = 0;
            UpdateSecondaryAdapters(now, stats);
        }
    }

    private void UpdateSystemProperties(SystemStats stats)
    {
        // Skip UI updates when view is not active
        if (!_isViewActive) return;

        // Update CPU properties (only format when integer part changes)
        CpuPercent = stats.Cpu.UsagePercent;
        var cpuInt = (int)stats.Cpu.UsagePercent;
        if (cpuInt != _lastCpuPercentInt)
        {
            _lastCpuPercentInt = cpuInt;
            CpuUsageFormatted = $"{stats.Cpu.UsagePercent:F0}%";
        }

        // Update Memory properties
        MemoryPercent = stats.Memory.UsagePercent;
        var memInt = (int)stats.Memory.UsagePercent;
        if (memInt != _lastMemPercentInt)
        {
            _lastMemPercentInt = memInt;
            MemoryUsageFormatted = $"{stats.Memory.UsagePercent:F0}%";
        }

        // Update overlay chart data if enabled
        if (ShowCpuOverlay || ShowMemoryOverlay)
        {
            var timestamp = stats.Timestamp;

            if (ShowCpuOverlay)
            {
                AddChartPoint(_cpuOverlayPoints, timestamp, stats.Cpu.UsagePercent);
            }

            if (ShowMemoryOverlay)
            {
                AddChartPoint(_memoryOverlayPoints, timestamp, stats.Memory.UsagePercent);
            }
        }
    }

    private static readonly string[] TrendDirectionNames =
        ["idle", "rising", "falling", "stable"];

    private void UpdateTrendIndicators(long downloadBps, long uploadBps)
    {
        var downloadTrend = _downloadTrendCalculator.Update(downloadBps);
        DownloadTrendIcon = downloadTrend.Icon;
        DownloadTrendText = TrendDirectionNames[(int)downloadTrend.Direction];

        var uploadTrend = _uploadTrendCalculator.Update(uploadBps);
        UploadTrendIcon = uploadTrend.Icon;
        UploadTrendText = TrendDirectionNames[(int)uploadTrend.Direction];
    }

    private void UpdatePeakSpeeds()
    {
        var peakDown = _chartDataManager.PeakDownloadBps;
        var peakUp = _chartDataManager.PeakUploadBps;
        if (_lastPeakDownloadBps != peakDown)
        {
            _lastPeakDownloadBps = peakDown;
            PeakDownloadSpeed = ByteFormatter.FormatSpeed(peakDown);
        }
        if (_lastPeakUploadBps != peakUp)
        {
            _lastPeakUploadBps = peakUp;
            PeakUploadSpeed = ByteFormatter.FormatSpeed(peakUp);
        }
    }

    #endregion

    #region Chart Management

    private void UpdateChart(DateTime now, long downloadBps, long uploadBps)
    {
        // Add points to chart
        AddChartPoint(_downloadSpeedPoints, now, downloadBps);
        AddChartPoint(_uploadSpeedPoints, now, uploadBps);

        // Update time range on X-axis
        var rangeSeconds = GetTimeRangeSeconds(SelectedTimeRange);
        if (ChartXAxes.Length > 0)
        {
            ChartXAxes[0].MinLimit = now.AddSeconds(-rangeSeconds).Ticks;
            ChartXAxes[0].MaxLimit = now.Ticks;
        }
    }

    private void AddChartPoint(BatchObservableCollection<DateTimePoint> points, DateTime timestamp, double value)
    {
        points.Add(new DateTimePoint(timestamp, value));

        // Keep only the last MaxHistoryPoints
        ChartCollectionHelper.TrimToMaxCount(points, MaxHistoryPoints);
    }

    /// <summary>
    /// Creates the base download/upload chart series, using the shared factory.
    /// </summary>
    private ISeries[] CreateChartSeries()
    {
        var baseSeries = ChartSeriesFactory.CreateSpeedLineSeries(_downloadSpeedPoints, _uploadSpeedPoints);
        // Ensure ScalesYAt = 0 (primary Y-axis)
        foreach (var s in baseSeries)
        {
            if (s is LineSeries<DateTimePoint> line)
                line.ScalesYAt = 0;
        }
        return baseSeries;
    }

    private void RebuildSecondaryAdapterSeries()
    {
        // Remove old secondary adapter series
        foreach (var series in _secondaryAdapterSeries)
            ChartSeries.Remove(series);
        _secondaryAdapterSeries.Clear();

        // Add new secondary adapter series
        var colorIndex = 0;
        foreach (var kvp in _secondaryAdapterPoints)
        {
            var adapterId = kvp.Key;
            var points = kvp.Value;
            var adapterInfo = SecondaryAdapters.FirstOrDefault(a => a.AdapterId == adapterId);
            var name = adapterInfo?.Name ?? adapterId;
            var isVpn = adapterInfo?.IsVpn ?? false;

            // Pick colors from palette, skip download/upload colors (indices 0,1)
            var color = ChartColors.SeriesPalette[(colorIndex + 2) % ChartColors.SeriesPalette.Length];

            var series = ChartSeriesFactory.CreateAdapterOverlayLineSeries(
                name, points, color, isVpn: isVpn);
            _secondaryAdapterSeries.Add(series);
            ChartSeries.Add(series);

            colorIndex++;
        }
    }

    private static int GetTimeRangeSeconds(TimeRange range) => range switch
    {
        TimeRange.OneMinute => 60,
        TimeRange.FiveMinutes => 300,
        TimeRange.FifteenMinutes => 900,
        TimeRange.OneHour => 3600,
        _ => 60
    };

    #endregion

    #region Partial Methods for Property Changes

    partial void OnShowCpuOverlayChanged(bool value)
    {
        if (value)
        {
            if (!ChartSeries.Contains(_cpuOverlaySeries))
                ChartSeries.Add(_cpuOverlaySeries);
        }
        else
        {
            _cpuOverlayPoints.Clear();
            ChartSeries.Remove(_cpuOverlaySeries);
        }
    }

    partial void OnShowMemoryOverlayChanged(bool value)
    {
        if (value)
        {
            if (!ChartSeries.Contains(_memoryOverlaySeries))
                ChartSeries.Add(_memoryOverlaySeries);
        }
        else
        {
            _memoryOverlayPoints.Clear();
            ChartSeries.Remove(_memoryOverlaySeries);
        }
    }

    partial void OnShowAdvancedAdaptersChanged(bool value)
    {
        LoadAdapters();
    }

    partial void OnSelectedAdapterChanged(AdapterDisplayItem? value)
    {
        if (value != null)
        {
            _networkMonitor.SetAdapter(value.Id);
        }
        else
        {
            _networkMonitor.SetAdapter(NetworkMonitorConstants.AutoAdapterId);
        }
    }

    #endregion

    #region Adapter Management

    private void LoadAdapters()
    {
        var networkAdapters = _networkMonitor.GetAdapters(ShowAdvancedAdapters)
            .Where(a => a.IsActive)
            .ToList();

        Adapters.Clear();

        // Add the Auto adapter as the first option
        Adapters.Add(AdapterDisplayItem.CreateAuto());

        foreach (var adapter in networkAdapters)
        {
            var displayItem = new AdapterDisplayItem(adapter);
            Adapters.Add(displayItem);
        }
    }

    private async Task RestoreSelectedAdapterAsync()
    {
        if (_dataPersistence == null)
        {
            SelectedAdapter = Adapters.FirstOrDefault();
            return;
        }

        try
        {
            var settings = await _dataPersistence.GetSettingsAsync();
            SelectedAdapter = Adapters.FirstOrDefault(a => a.Id == settings.SelectedAdapterId)
                              ?? Adapters.FirstOrDefault();
        }
        catch
        {
            SelectedAdapter = Adapters.FirstOrDefault();
        }
    }

    #endregion

    #region Secondary Adapters & Auto Switch

    private void UpdateSecondaryAdapters(DateTime now, NetworkStats stats)
    {
        var allStats = _networkMonitor.GetAllAdapterStats();
        var adapters = _networkMonitor.GetAdapters(ShowAdvancedAdapters);
        var resolvedPrimaryId = stats.ResolvedPrimaryAdapterId;

        // Determine the "primary" adapter ID to exclude from secondary list
        var primaryId = SelectedAdapter?.IsAuto == true
            ? resolvedPrimaryId
            : SelectedAdapter?.Id ?? string.Empty;

        var activeSecondaryIds = new HashSet<string>();
        var needsRebuild = false;

        foreach (var kvp in allStats)
        {
            var adapterId = kvp.Key;
            var adapterStats = kvp.Value;

            // Skip the primary adapter
            if (adapterId == primaryId)
                continue;

            // Only include adapters with active traffic
            if (adapterStats.DownloadSpeedBps <= 0 && adapterStats.UploadSpeedBps <= 0)
                continue;

            var adapter = adapters.FirstOrDefault(a => a.Id == adapterId);
            if (adapter == null) continue;

            activeSecondaryIds.Add(adapterId);

            // Try to update existing entry in-place
            var existing = SecondaryAdapters.FirstOrDefault(a => a.AdapterId == adapterId);
            if (existing != null)
            {
                existing.DownloadSpeed = ByteFormatter.FormatSpeed(adapterStats.DownloadSpeedBps);
                existing.UploadSpeed = ByteFormatter.FormatSpeed(adapterStats.UploadSpeedBps);
                existing.DownloadBps = adapterStats.DownloadSpeedBps;
                existing.UploadBps = adapterStats.UploadSpeedBps;
            }
            else
            {
                // New adapter — needs full rebuild
                needsRebuild = true;
            }

            // Add chart data point for this secondary adapter
            if (!_secondaryAdapterPoints.ContainsKey(adapterId))
            {
                _secondaryAdapterPoints[adapterId] = new BatchObservableCollection<DateTimePoint>();
            }
            AddChartPoint(_secondaryAdapterPoints[adapterId], now, adapterStats.DownloadSpeedBps);
        }

        // Check for removed adapters
        var staleIds = _secondaryAdapterPoints.Keys.Where(id => !activeSecondaryIds.Contains(id)).ToList();
        if (staleIds.Count > 0)
        {
            foreach (var staleId in staleIds)
                _secondaryAdapterPoints.Remove(staleId);
            needsRebuild = true;
        }

        // Only rebuild the collection when the adapter set actually changed
        if (needsRebuild)
        {
            var activeList = new List<SecondaryAdapterInfo>();
            foreach (var adapterId in activeSecondaryIds)
            {
                var adapterStats = allStats[adapterId];
                var adapter = adapters.FirstOrDefault(a => a.Id == adapterId);
                if (adapter == null) continue;

                activeList.Add(new SecondaryAdapterInfo
                {
                    AdapterId = adapterId,
                    Name = adapter.DisplayName,
                    Icon = adapter.IsKnownVpn ? "🔐" : adapter.AdapterType switch
                    {
                        NetworkAdapterType.WiFi => "📶",
                        NetworkAdapterType.Ethernet => "🔌",
                        _ => "🌐"
                    },
                    DownloadSpeed = ByteFormatter.FormatSpeed(adapterStats.DownloadSpeedBps),
                    UploadSpeed = ByteFormatter.FormatSpeed(adapterStats.UploadSpeedBps),
                    DownloadBps = adapterStats.DownloadSpeedBps,
                    UploadBps = adapterStats.UploadSpeedBps,
                    IsVpn = adapter.IsKnownVpn,
                    ColorHex = adapter.IsKnownVpn ? "#A855F7" : "#3B82F6"
                });
            }

            SecondaryAdapters = new ObservableCollection<SecondaryAdapterInfo>(activeList);
            HasSecondaryAdapters = activeList.Count > 0;
            RebuildSecondaryAdapterSeries();
        }
    }

    private async void ShowAutoSwitchNotificationAsync(string adapterName)
    {
        _notificationCts?.Cancel();
        _notificationCts = new CancellationTokenSource();
        var token = _notificationCts.Token;

        AutoSwitchNotification = $"Switched to {adapterName}";
        IsAutoSwitchNotificationVisible = true;

        try
        {
            await Task.Delay(3000, token);
            if (!token.IsCancellationRequested)
            {
                IsAutoSwitchNotificationVisible = false;
                AutoSwitchNotification = string.Empty;
            }
        }
        catch (TaskCanceledException)
        {
            // New notification replaced this one
        }
    }

    #endregion

    #region Data Loading

    private async Task LoadTodayUsageAsync()
    {
        if (_dataPersistence == null) return;

        try
        {
            var (received, sent) = await _dataPersistence.GetTodayUsageAsync();
            _todayStoredReceived = received;
            _todayStoredSent = sent;

            // Initialize formatted properties so they're correct before first stats event
            if (received > 0) TodayDownload = ByteFormatter.FormatBytes(received);
            if (sent > 0) TodayUpload = ByteFormatter.FormatBytes(sent);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to load stored usage data");
        }
    }

    #endregion

    #region Commands

    [RelayCommand]
    private void ToggleCpuOverlay()
    {
        ShowCpuOverlay = !ShowCpuOverlay;
    }

    [RelayCommand]
    private void ToggleMemoryOverlay()
    {
        ShowMemoryOverlay = !ShowMemoryOverlay;
    }

    [RelayCommand]
    private void SetTimeRange(TimeRange range)
    {
        SelectedTimeRange = range;
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _networkMonitor.StatsUpdated -= OnNetworkStatsUpdated;
        _systemMonitor.StatsUpdated -= OnSystemStatsUpdated;
        _navigationService.NavigationChanged -= OnNavigationChanged;
        _notificationCts?.Cancel();
        _notificationCts?.Dispose();
    }
}

/// <summary>
/// Display item for time range selection
/// </summary>
public sealed record TimeRangeDisplayItem(
    TimeRange Value,
    string Label,
    string Description,
    int Seconds);
