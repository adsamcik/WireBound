using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using WireBound.Avalonia.Helpers;
using WireBound.Core;
using WireBound.Core.Helpers;
using WireBound.Core.Services;
using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Avalonia.ViewModels;

public sealed partial class AppsViewModel : ObservableObject, IDisposable
{
    private const string AllCategories = "All";
    private readonly IUiDispatcher _dispatcher;
    private readonly IAppOverviewService _appOverviewService;
    private readonly IElevationService _elevationService;
    private readonly INavigationService _navigationService;
    private readonly ILogger<AppsViewModel>? _logger;
    private readonly CancellationTokenSource _cts = new();
    private CancellationTokenSource? _searchDebounceCts;
    private CancellationTokenSource? _detailLoadCts;
    private IReadOnlyList<AppOverview> _loadedApps = [];
    private bool _disposed;
    private bool _isUpdatingSelection;
    private bool _suppressRecompute;

    public Task InitializationTask { get; }

    /// <summary>
    /// Raised after a sort change rearranges <see cref="Apps"/>. The view
    /// subscribes to scroll the master ListBox back to the top so the user
    /// always sees the top of the new ordering rather than whatever rows
    /// happen to land at the previous pixel offset.
    /// </summary>
    public event Action? ResetListScrollRequested;

    [ObservableProperty]
    private DateTime? _startDate = DateTime.Now.Date;

    [ObservableProperty]
    private DateTime? _endDate = DateTime.Now;

    [ObservableProperty]
    private BatchObservableCollection<AppOverview> _apps = new();

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string _selectedCategory = AllCategories;

    [ObservableProperty]
    private ObservableCollection<string> _availableCategories = new() { AllCategories };

    [ObservableProperty]
    private AppsSortColumn _sortColumn = AppsSortColumn.TotalBytes;

    [ObservableProperty]
    private bool _sortDescending = true;

    [ObservableProperty]
    private int _appCount;

    [ObservableProperty]
    private string _totalDownload = "0 B";

    [ObservableProperty]
    private string _totalUpload = "0 B";

    [ObservableProperty]
    private string _topCpuApp = "—";

    [ObservableProperty]
    private string _topMemoryApp = "—";

    /// <summary>
    /// Count of apps in the filtered + sorted view (drives the APPS COUNT card).
    /// Distinct from the total returned by the data layer — set by RecomputeView,
    /// not LoadOverviewAsync.
    /// </summary>
    [ObservableProperty]
    private int _shownAppCount;

    /// <summary>True when at least one active filter is narrowing the visible set.</summary>
    [ObservableProperty]
    private bool _hasActiveFilters;

    /// <summary>
    /// Minimum total bytes (down + up) an app must exceed to appear in the list.
    /// 0 disables the filter. Bound to a ComboBox of bucketed values.
    /// </summary>
    [ObservableProperty]
    private long _minTotalBytes;

    /// <summary>
    /// Time window an app's <see cref="AppOverview.LastSeen"/> must fall into
    /// for it to appear. <see cref="AppsActivityWindow.Any"/> disables the filter.
    /// </summary>
    [ObservableProperty]
    private AppsActivityWindow _activityWindow = AppsActivityWindow.Any;

    /// <summary>
    /// Coarse activity mode filter. All / HasNetworkActivity / ResourceOnly.
    /// Lets users isolate apps that actually moved bytes vs background services
    /// only showing up in resource snapshots.
    /// </summary>
    [ObservableProperty]
    private AppsActivityMode _activityMode = AppsActivityMode.All;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isInitialLoading = true;

    [ObservableProperty]
    private bool _isPlatformSupported = true;

    [ObservableProperty]
    private bool _requiresElevation;

    [ObservableProperty]
    private bool _isByteTrackingLimited;

    [ObservableProperty]
    private bool _isRequestingElevation;

    [ObservableProperty]
    private AppOverview? _selectedApp;

    [ObservableProperty]
    private bool _isDetailOpen;

    [ObservableProperty]
    private ISeries[] _networkHistorySeries = [];

    [ObservableProperty]
    private Axis[] _networkHistoryXAxes = [];

    [ObservableProperty]
    private Axis[] _networkHistoryYAxes = [];

    [ObservableProperty]
    private ISeries[] _cpuHistorySeries = [];

    [ObservableProperty]
    private Axis[] _cpuHistoryXAxes = [];

    [ObservableProperty]
    private Axis[] _cpuHistoryYAxes = [];

    [ObservableProperty]
    private ISeries[] _ramHistorySeries = [];

    [ObservableProperty]
    private Axis[] _ramHistoryXAxes = [];

    [ObservableProperty]
    private Axis[] _ramHistoryYAxes = [];

    [ObservableProperty]
    private BatchObservableCollection<TopDestinationEntry> _topDestinations = new();

    [ObservableProperty]
    private BatchObservableCollection<TopDestinationDisplayEntry> _topDestinationDisplayItems = new();

    public bool ShowStatusBanner => !IsPlatformSupported || RequiresElevation || IsByteTrackingLimited;
    public string SelectedAppDisplayName => SelectedApp is null ? "—" : GetDisplayName(SelectedApp);
    public string SelectedAppExecutablePath => SelectedApp?.ExecutablePath ?? "";
    public string SelectedAppProcessName => SelectedApp?.ProcessName ?? "";
    public string SelectedAppCategoryName => string.IsNullOrWhiteSpace(SelectedApp?.CategoryName) ? "Uncategorized" : SelectedApp.CategoryName;
    public string SelectedAppAvgCpuPercent => SelectedApp is null ? "0.0%" : $"{SelectedApp.AvgCpuPercent:F1}%";
    public string SelectedAppAvgRam => SelectedApp?.FormattedAvgPrivateBytes ?? "0 B";
    public string SelectedAppTotalBytes => SelectedApp?.FormattedTotalBytes ?? "0 B";
    public string SelectedAppBytesReceived => SelectedApp?.FormattedBytesReceived ?? "0 B";
    public string SelectedAppBytesSent => SelectedApp?.FormattedBytesSent ?? "0 B";
    public string? SelectedAppIconPath => SelectedApp?.IconPath;
    public bool SelectedAppHasIcon => SelectedApp?.HasIcon == true;
    public int SelectedAppHoursActive => SelectedApp?.HoursActive ?? 0;
    public string NameSortGlyph => GetSortGlyph(AppsSortColumn.Name);
    public string CategorySortGlyph => GetSortGlyph(AppsSortColumn.Category);
    public string BytesReceivedSortGlyph => GetSortGlyph(AppsSortColumn.BytesReceived);
    public string BytesSentSortGlyph => GetSortGlyph(AppsSortColumn.BytesSent);
    public string TotalBytesSortGlyph => GetSortGlyph(AppsSortColumn.TotalBytes);
    public string AvgCpuPercentSortGlyph => GetSortGlyph(AppsSortColumn.AvgCpuPercent);
    public string AvgPrivateBytesSortGlyph => GetSortGlyph(AppsSortColumn.AvgPrivateBytes);
    public string HoursActiveSortGlyph => GetSortGlyph(AppsSortColumn.HoursActive);

    public AppsViewModel(
        IUiDispatcher dispatcher,
        IAppOverviewService appOverviewService,
        IElevationService elevationService,
        INavigationService navigationService,
        ILogger<AppsViewModel>? logger = null)
    {
        _dispatcher = dispatcher;
        _appOverviewService = appOverviewService;
        _elevationService = elevationService;
        _navigationService = navigationService;
        _logger = logger;

        IsPlatformSupported = _elevationService.IsElevationSupported
                              || !_elevationService.RequiresElevationFor(ElevatedFeature.PerProcessNetworkMonitoring);
        RequiresElevation = _elevationService.RequiresElevationFor(ElevatedFeature.PerProcessNetworkMonitoring)
                            && _elevationService.IsElevationSupported
                            && !_elevationService.IsHelperConnected;
        IsByteTrackingLimited = !_elevationService.IsHelperConnected;

        _elevationService.HelperConnectionStateChanged += OnHelperConnectionStateChanged;
        _navigationService.NavigationChanged += OnNavigationChanged;

        InitializationTask = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            await LoadOverviewAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize apps view");
        }
    }

    private void OnNavigationChanged(string route)
    {
        if (route != Routes.Apps || IsInitialLoading || Apps.Count > 0)
        {
            return;
        }

        _ = RefreshAsync();
    }

    private void OnHelperConnectionStateChanged(object? sender, HelperConnectionStateChangedEventArgs e)
    {
        _dispatcher.Post(() =>
        {
            RequiresElevation = !e.IsConnected
                               && _elevationService.IsElevationSupported
                               && _elevationService.RequiresElevationFor(ElevatedFeature.PerProcessNetworkMonitoring);
            IsByteTrackingLimited = !e.IsConnected;
        }, UiDispatcherPriority.Background);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadOverviewAsync();
    }

    [RelayCommand]
    private void ToggleSort(AppsSortColumn column)
    {
        _suppressRecompute = true;
        try
        {
            if (SortColumn == column)
            {
                SortDescending = !SortDescending;
            }
            else
            {
                SortColumn = column;
                SortDescending = true;
            }
        }
        finally
        {
            _suppressRecompute = false;
        }

        NotifySortGlyphsChanged();
        RecomputeView();
        ResetListScrollRequested?.Invoke();
    }

    /// <summary>
    /// One-click sort preset for the chip strip above the master list. Each
    /// preset sets both the sort column and direction in one batched update
    /// so observers (UI, ResetListScrollRequested) fire exactly once.
    /// </summary>
    [RelayCommand]
    private void ApplySortPreset(string preset)
    {
        var (column, descending) = preset switch
        {
            "traffic" => (AppsSortColumn.TotalBytes, true),
            "download" => (AppsSortColumn.BytesReceived, true),
            "upload" => (AppsSortColumn.BytesSent, true),
            "cpu" => (AppsSortColumn.AvgCpuPercent, true),
            "ram" => (AppsSortColumn.AvgPrivateBytes, true),
            "recent" => (AppsSortColumn.LastSeen, true),
            "name" => (AppsSortColumn.Name, false),
            _ => (SortColumn, SortDescending)
        };

        _suppressRecompute = true;
        try
        {
            SortColumn = column;
            SortDescending = descending;
        }
        finally
        {
            _suppressRecompute = false;
        }

        NotifySortGlyphsChanged();
        RecomputeView();
        ResetListScrollRequested?.Invoke();
    }

    /// <summary>
    /// Snaps the date range to a common preset (today / 24h / 7d / 30d) and
    /// reloads. Saves users from clicking through two date pickers for the
    /// vast majority of scoping decisions.
    /// </summary>
    [RelayCommand]
    private async Task ApplyDatePresetAsync(string preset)
    {
        var now = DateTime.Now;
        (DateTime start, DateTime end) = preset switch
        {
            "today" => (now.Date, now),
            "24h" => (now.AddHours(-24), now),
            "7d" => (now.AddDays(-7), now),
            "30d" => (now.AddDays(-30), now),
            _ => (StartDate ?? now.Date, EndDate ?? now)
        };

        _suppressRecompute = true;
        try
        {
            StartDate = start;
            EndDate = end;
        }
        finally
        {
            _suppressRecompute = false;
        }

        await LoadOverviewAsync();
    }

    /// <summary>Resets every filter (but not sort / date range) back to its default.</summary>
    [RelayCommand]
    private void ClearFilters()
    {
        _suppressRecompute = true;
        try
        {
            SearchText = string.Empty;
            SelectedCategory = AllCategories;
            MinTotalBytes = 0;
            ActivityWindow = AppsActivityWindow.Any;
            ActivityMode = AppsActivityMode.All;
        }
        finally
        {
            _suppressRecompute = false;
        }

        RecomputeView();
    }

    [RelayCommand]
    private async Task SelectAppAsync(AppOverview app)
    {
        if (_disposed || app is null) return;

        _detailLoadCts?.Cancel();
        _detailLoadCts?.Dispose();
        _detailLoadCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        var token = _detailLoadCts.Token;

        _isUpdatingSelection = true;
        try
        {
            if (!ReferenceEquals(SelectedApp, app))
            {
                SelectedApp = app;
            }

            IsDetailOpen = true;
            ClearDetailCharts();
        }
        finally
        {
            _isUpdatingSelection = false;
        }

        try
        {
            var (start, end) = GetDateRange();
            var networkTask = _appOverviewService.GetNetworkHistoryAsync(app.AppIdentifier, start, end, token);
            var resourceTask = _appOverviewService.GetResourceHistoryAsync(app.AppIdentifier, start, end, token);
            var destinationsTask = _appOverviewService.GetTopDestinationsAsync(15, start, end, token);

            await Task.WhenAll(networkTask, resourceTask, destinationsTask);

            if (token.IsCancellationRequested || SelectedApp?.AppIdentifier != app.AppIdentifier)
            {
                return;
            }

            var network = await networkTask;
            var resources = await resourceTask;
            var destinations = await destinationsTask;

            await _dispatcher.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested || SelectedApp?.AppIdentifier != app.AppIdentifier)
                {
                    return;
                }

                NetworkHistoryXAxes = CreateTimeAxes();
                NetworkHistoryYAxes = CreateByteYAxes("Bytes");
                NetworkHistorySeries = CreateNetworkSeries(network);

                CpuHistoryXAxes = CreateTimeAxes();
                CpuHistoryYAxes = CreatePercentageYAxes();
                CpuHistorySeries = CreateCpuSeries(resources);

                RamHistoryXAxes = CreateTimeAxes();
                RamHistoryYAxes = CreateByteYAxes("RAM");
                RamHistorySeries = CreateRamSeries(resources);

                var topDestinations = destinations.Take(15).ToList();
                TopDestinations.ReplaceAll(topDestinations);
                TopDestinationDisplayItems.ReplaceAll(topDestinations.Select(ToDisplayEntry));
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load app detail for {AppIdentifier}", app.AppIdentifier);
        }
    }

    [RelayCommand]
    private void CloseDetail()
    {
        _detailLoadCts?.Cancel();

        _isUpdatingSelection = true;
        try
        {
            SelectedApp = null;
            IsDetailOpen = false;
            ClearDetailCharts();
            TopDestinations.ReplaceAll([]);
            TopDestinationDisplayItems.ReplaceAll([]);
        }
        finally
        {
            _isUpdatingSelection = false;
        }
    }

    [RelayCommand]
    private async Task RequestElevationAsync()
    {
        if (!_elevationService.IsElevationSupported)
        {
            _logger?.LogWarning("Elevation requested but not supported on this platform");
            return;
        }

        if (_elevationService.IsHelperConnected)
        {
            _logger?.LogDebug("Helper already connected, no elevation needed");
            return;
        }

        IsRequestingElevation = true;
        try
        {
            _logger?.LogInformation("User requested to start elevated helper from Apps view");

            var result = await _elevationService.StartHelperAsync(_cts.Token);

            if (result.IsSuccess)
            {
                _logger?.LogInformation("Helper process started successfully");
                RequiresElevation = false;
                IsByteTrackingLimited = false;
            }
            else if (result.Status == ElevationStatus.Cancelled)
            {
                _logger?.LogInformation("User cancelled helper elevation request");
            }
            else
            {
                _logger?.LogWarning("Failed to start helper: {Error}", result.ErrorMessage);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unexpected error during helper start request");
        }
        finally
        {
            IsRequestingElevation = false;
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        if (_disposed) return;

        _searchDebounceCts?.Cancel();
        _searchDebounceCts?.Dispose();
        _searchDebounceCts = new CancellationTokenSource();

        var token = _searchDebounceCts.Token;
        _ = DebouncedRecomputeAsync(token);
    }

    partial void OnSelectedCategoryChanged(string value)
    {
        if (!_suppressRecompute)
        {
            RecomputeView();
        }
    }

    partial void OnMinTotalBytesChanged(long value)
    {
        if (!_suppressRecompute)
        {
            RecomputeView();
        }
    }

    partial void OnActivityWindowChanged(AppsActivityWindow value)
    {
        if (!_suppressRecompute)
        {
            RecomputeView();
        }
    }

    partial void OnActivityModeChanged(AppsActivityMode value)
    {
        if (!_suppressRecompute)
        {
            RecomputeView();
        }
    }

    partial void OnSortColumnChanged(AppsSortColumn value)
    {
        NotifySortGlyphsChanged();
        if (!_suppressRecompute)
        {
            RecomputeView();
            // Re-sort changes which row is at any given scroll offset, so the
            // viewport's content is essentially random after the rearrange.
            // Resetting to the top of the new ordering is the user expectation
            // for table-style headers.
            ResetListScrollRequested?.Invoke();
        }
    }

    partial void OnSortDescendingChanged(bool value)
    {
        NotifySortGlyphsChanged();
        if (!_suppressRecompute)
        {
            RecomputeView();
            ResetListScrollRequested?.Invoke();
        }
    }

    partial void OnIsPlatformSupportedChanged(bool value) => OnPropertyChanged(nameof(ShowStatusBanner));
    partial void OnRequiresElevationChanged(bool value) => OnPropertyChanged(nameof(ShowStatusBanner));
    partial void OnIsByteTrackingLimitedChanged(bool value) => OnPropertyChanged(nameof(ShowStatusBanner));

    partial void OnSelectedAppChanged(AppOverview? value)
    {
        NotifySelectedAppPropertiesChanged();

        if (_isUpdatingSelection) return;

        if (value is null)
        {
            CloseDetail();
            return;
        }

        _ = SelectAppAsync(value);
    }

    private async Task LoadOverviewAsync()
    {
        if (_disposed) return;

        IsLoading = true;
        try
        {
            var (start, end) = GetDateRange();
            var apps = await _appOverviewService.GetOverviewAsync(start, end, _cts.Token);

            if (_cts.Token.IsCancellationRequested) return;

            await _dispatcher.InvokeAsync(() =>
            {
                _loadedApps = apps;
                UpdateAvailableCategories(apps);
                // Don't UpdateSummary(apps) directly — RecomputeView reapplies
                // filters and calls UpdateSummary with the filtered result, so
                // the cards stay in sync with the visible list.
                RecomputeView();

                if (SelectedApp is not null && apps.All(a => a.AppIdentifier != SelectedApp.AppIdentifier))
                {
                    CloseDetail();
                }
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load app overview data");
        }
        finally
        {
            IsLoading = false;
            IsInitialLoading = false;
        }
    }

    private async Task DebouncedRecomputeAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(200, token);
            if (token.IsCancellationRequested || _disposed) return;

            await _dispatcher.InvokeAsync(RecomputeView);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void RecomputeView()
    {
        if (_disposed) return;

        // Materialize the filtered + sorted result FIRST so the summary cards
        // can reflect the visible subset, not the whole dataset. This way the
        // user's TOTAL DOWNLOAD / UPLOAD cards stay honest under any filter
        // combination.
        var filtered = _loadedApps.Where(MatchesActiveFilters).ToList();

        IEnumerable<AppOverview> ordered = SortDescending
            ? filtered.OrderByDescending(GetSortKey).ThenBy(app => GetDisplayName(app), StringComparer.OrdinalIgnoreCase)
            : filtered.OrderBy(GetSortKey).ThenBy(app => GetDisplayName(app), StringComparer.OrdinalIgnoreCase);

        var materialized = ordered.ToList();
        Apps.ReplaceAll(materialized);
        UpdateSummary(materialized);
        HasActiveFilters = ComputeHasActiveFilters();
    }

    private bool ComputeHasActiveFilters()
        => !string.IsNullOrWhiteSpace(SearchText)
           || !string.Equals(SelectedCategory, AllCategories, StringComparison.OrdinalIgnoreCase)
           || MinTotalBytes > 0
           || ActivityWindow != AppsActivityWindow.Any
           || ActivityMode != AppsActivityMode.All;

    private object GetSortKey(AppOverview app)
    {
        return SortColumn switch
        {
            AppsSortColumn.Name => GetDisplayName(app),
            AppsSortColumn.Category => app.CategoryName,
            AppsSortColumn.BytesReceived => app.BytesReceived,
            AppsSortColumn.BytesSent => app.BytesSent,
            AppsSortColumn.TotalBytes => app.TotalBytes,
            AppsSortColumn.AvgCpuPercent => app.AvgCpuPercent,
            AppsSortColumn.AvgPrivateBytes => app.AvgPrivateBytes,
            AppsSortColumn.HoursActive => app.HoursActive,
            AppsSortColumn.LastSeen => app.LastSeen.Ticks,
            _ => app.TotalBytes
        };
    }

    /// <summary>
    /// Returns true when the given app passes the currently-active filter
    /// constraints (search, category, min bytes, activity window, activity
    /// mode). Centralized so RecomputeView and HasActiveFilters stay in sync.
    /// </summary>
    private bool MatchesActiveFilters(AppOverview app)
    {
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText.Trim();
            if (!Contains(app.AppName, search) &&
                !Contains(app.ProcessName, search) &&
                !Contains(app.ExecutablePath, search) &&
                !Contains(app.CategoryName, search))
            {
                return false;
            }
        }

        if (!string.Equals(SelectedCategory, AllCategories, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(app.CategoryName, SelectedCategory, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (MinTotalBytes > 0 && app.TotalBytes < MinTotalBytes)
        {
            return false;
        }

        if (ActivityWindow != AppsActivityWindow.Any)
        {
            var cutoff = DateTime.Now - ActivityWindow switch
            {
                AppsActivityWindow.LastHour => TimeSpan.FromHours(1),
                AppsActivityWindow.Last24Hours => TimeSpan.FromHours(24),
                AppsActivityWindow.Last7Days => TimeSpan.FromDays(7),
                _ => TimeSpan.Zero
            };
            if (app.LastSeen < cutoff)
            {
                return false;
            }
        }

        if (ActivityMode == AppsActivityMode.HasNetworkActivity && app.TotalBytes <= 0)
        {
            return false;
        }
        if (ActivityMode == AppsActivityMode.ResourceOnly && app.TotalBytes > 0)
        {
            return false;
        }

        return true;
    }

    private void UpdateAvailableCategories(IReadOnlyList<AppOverview> apps)
    {
        var categories = apps
            .Select(a => a.CategoryName)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .Prepend(AllCategories)
            .ToList();

        AvailableCategories = new ObservableCollection<string>(categories);

        if (!categories.Contains(SelectedCategory, StringComparer.OrdinalIgnoreCase))
        {
            SelectedCategory = AllCategories;
        }
    }

    private void UpdateSummary(IReadOnlyList<AppOverview> apps)
    {
        AppCount = apps.Count;
        ShownAppCount = apps.Count;
        TotalDownload = ByteFormatter.FormatBytes(apps.Sum(a => a.BytesReceived));
        TotalUpload = ByteFormatter.FormatBytes(apps.Sum(a => a.BytesSent));
        TopCpuApp = apps.Count == 0 ? "—" : GetDisplayName(apps.MaxBy(a => a.AvgCpuPercent)!);
        TopMemoryApp = apps.Count == 0 ? "—" : GetDisplayName(apps.MaxBy(a => a.AvgPrivateBytes)!);
    }

    private (DateOnly Start, DateOnly End) GetDateRange()
    {
        var start = DateOnly.FromDateTime(StartDate ?? DateTime.Now.Date);
        var end = DateOnly.FromDateTime(EndDate ?? DateTime.Now);

        return start <= end ? (start, end) : (end, start);
    }

    private void ClearDetailCharts()
    {
        NetworkHistorySeries = [];
        NetworkHistoryXAxes = [];
        NetworkHistoryYAxes = [];
        CpuHistorySeries = [];
        CpuHistoryXAxes = [];
        CpuHistoryYAxes = [];
        RamHistorySeries = [];
        RamHistoryXAxes = [];
        RamHistoryYAxes = [];
    }

    private static bool Contains(string? source, string search)
    {
        return source?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string GetDisplayName(AppOverview app)
    {
        if (!string.IsNullOrWhiteSpace(app.AppName)) return app.AppName;
        if (!string.IsNullOrWhiteSpace(app.ProcessName)) return app.ProcessName;
        return app.AppIdentifier;
    }

    private string GetSortGlyph(AppsSortColumn column)
    {
        if (SortColumn != column) return "";
        return SortDescending ? "▼" : "▲";
    }

    private void NotifySortGlyphsChanged()
    {
        OnPropertyChanged(nameof(NameSortGlyph));
        OnPropertyChanged(nameof(CategorySortGlyph));
        OnPropertyChanged(nameof(BytesReceivedSortGlyph));
        OnPropertyChanged(nameof(BytesSentSortGlyph));
        OnPropertyChanged(nameof(TotalBytesSortGlyph));
        OnPropertyChanged(nameof(AvgCpuPercentSortGlyph));
        OnPropertyChanged(nameof(AvgPrivateBytesSortGlyph));
        OnPropertyChanged(nameof(HoursActiveSortGlyph));
    }

    private void NotifySelectedAppPropertiesChanged()
    {
        OnPropertyChanged(nameof(SelectedAppDisplayName));
        OnPropertyChanged(nameof(SelectedAppExecutablePath));
        OnPropertyChanged(nameof(SelectedAppProcessName));
        OnPropertyChanged(nameof(SelectedAppCategoryName));
        OnPropertyChanged(nameof(SelectedAppAvgCpuPercent));
        OnPropertyChanged(nameof(SelectedAppAvgRam));
        OnPropertyChanged(nameof(SelectedAppTotalBytes));
        OnPropertyChanged(nameof(SelectedAppBytesReceived));
        OnPropertyChanged(nameof(SelectedAppBytesSent));
        OnPropertyChanged(nameof(SelectedAppIconPath));
        OnPropertyChanged(nameof(SelectedAppHasIcon));
        OnPropertyChanged(nameof(SelectedAppHoursActive));
    }

    private static ISeries[] CreateNetworkSeries(IReadOnlyList<AppNetworkHistoryPoint> points)
    {
        var ordered = points.OrderBy(p => p.Timestamp).ToArray();
        return
        [
            new LineSeries<DateTimePoint>
            {
                Name = "Download",
                Values = ordered.Select(p => new DateTimePoint(p.Timestamp, p.BytesReceived)).ToArray(),
                Fill = null,
                Stroke = new SolidColorPaint(ChartColors.DownloadColor, 2.5f),
                GeometrySize = 4,
                GeometryFill = new SolidColorPaint(ChartColors.DownloadColor),
                GeometryStroke = null,
                LineSmoothness = 0.35
            },
            new LineSeries<DateTimePoint>
            {
                Name = "Upload",
                Values = ordered.Select(p => new DateTimePoint(p.Timestamp, p.BytesSent)).ToArray(),
                Fill = null,
                Stroke = new SolidColorPaint(ChartColors.UploadColor, 2.5f),
                GeometrySize = 4,
                GeometryFill = new SolidColorPaint(ChartColors.UploadColor),
                GeometryStroke = null,
                LineSmoothness = 0.35
            }
        ];
    }

    private static ISeries[] CreateCpuSeries(IReadOnlyList<AppResourceHistoryPoint> points)
    {
        var ordered = points.OrderBy(p => p.Timestamp).ToArray();
        return
        [
            new LineSeries<DateTimePoint>
            {
                Name = "CPU %",
                Values = ordered.Select(p => new DateTimePoint(p.Timestamp, p.CpuPercent)).ToArray(),
                Fill = new SolidColorPaint(ChartColors.CpuColor.WithAlpha(30)),
                Stroke = new SolidColorPaint(ChartColors.CpuColor, 2.5f),
                GeometrySize = 4,
                GeometryFill = new SolidColorPaint(ChartColors.CpuColor),
                GeometryStroke = null,
                LineSmoothness = 0.35
            }
        ];
    }

    private static ISeries[] CreateRamSeries(IReadOnlyList<AppResourceHistoryPoint> points)
    {
        var ordered = points.OrderBy(p => p.Timestamp).ToArray();
        return
        [
            new LineSeries<DateTimePoint>
            {
                Name = "RAM",
                Values = ordered.Select(p => new DateTimePoint(p.Timestamp, p.PrivateBytes)).ToArray(),
                Fill = new SolidColorPaint(ChartColors.MemoryColor.WithAlpha(30)),
                Stroke = new SolidColorPaint(ChartColors.MemoryColor, 2.5f),
                GeometrySize = 4,
                GeometryFill = new SolidColorPaint(ChartColors.MemoryColor),
                GeometryStroke = null,
                LineSmoothness = 0.35
            }
        ];
    }

    private static Axis[] CreateTimeAxes()
    {
        return
        [
            new DateTimeAxis(TimeSpan.FromHours(1), date => date.ToString("MM/dd HH:mm"))
            {
                Name = "Time",
                NamePaint = new SolidColorPaint(ChartColors.AxisNameColor),
                LabelsPaint = new SolidColorPaint(ChartColors.AxisLabelColor),
                TextSize = 10,
                NameTextSize = 11,
                LabelsRotation = -30,
                SeparatorsPaint = new SolidColorPaint(ChartColors.GridLineColor)
            }
        ];
    }

    private static Axis[] CreateByteYAxes(string name)
    {
        return
        [
            new Axis
            {
                Name = name,
                NamePaint = new SolidColorPaint(ChartColors.AxisNameColor),
                LabelsPaint = new SolidColorPaint(ChartColors.AxisLabelColor),
                TextSize = 11,
                NameTextSize = 12,
                MinLimit = 0,
                SeparatorsPaint = new SolidColorPaint(ChartColors.GridLineColor),
                Labeler = value => ByteFormatter.FormatBytesCompact((long)value)
            }
        ];
    }

    private static Axis[] CreatePercentageYAxes()
    {
        return
        [
            new Axis
            {
                Name = "CPU %",
                NamePaint = new SolidColorPaint(ChartColors.AxisNameColor),
                LabelsPaint = new SolidColorPaint(ChartColors.AxisLabelColor),
                TextSize = 11,
                NameTextSize = 12,
                MinLimit = 0,
                MaxLimit = 100,
                SeparatorsPaint = new SolidColorPaint(ChartColors.GridLineColor),
                Labeler = value => $"{value:F0}%"
            }
        ];
    }

    private static TopDestinationDisplayEntry ToDisplayEntry(TopDestinationEntry entry)
    {
        var endpoint = string.IsNullOrWhiteSpace(entry.Hostname) ? entry.RemoteAddress : entry.Hostname!;
        var details = $"{entry.RemoteAddress}:{entry.Port} {entry.Protocol}";
        return new TopDestinationDisplayEntry(endpoint, details, ByteFormatter.FormatBytes(entry.TotalBytes));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        _cts.Dispose();
        _searchDebounceCts?.Cancel();
        _searchDebounceCts?.Dispose();
        _detailLoadCts?.Cancel();
        _detailLoadCts?.Dispose();
        _elevationService.HelperConnectionStateChanged -= OnHelperConnectionStateChanged;
        _navigationService.NavigationChanged -= OnNavigationChanged;
    }
}

public enum AppsSortColumn
{
    Name,
    Category,
    BytesReceived,
    BytesSent,
    TotalBytes,
    AvgCpuPercent,
    AvgPrivateBytes,
    HoursActive,
    LastSeen
}

/// <summary>
/// Recent-activity buckets for the Apps tab's filter strip. Maps to a TimeSpan
/// used against <see cref="AppOverview.LastSeen"/>; <c>Any</c> disables the
/// filter so apps active anywhere in the selected date range pass through.
/// </summary>
public enum AppsActivityWindow
{
    Any,
    LastHour,
    Last24Hours,
    Last7Days
}

/// <summary>
/// Coarse split between apps that produced network traffic and apps that only
/// appear in resource snapshots. <c>HasNetworkActivity</c> hides background
/// services that never moved bytes; <c>ResourceOnly</c> isolates the opposite.
/// </summary>
public enum AppsActivityMode
{
    All,
    HasNetworkActivity,
    ResourceOnly
}

public sealed record TopDestinationDisplayEntry(string Endpoint, string Details, string FormattedTotalBytes);
