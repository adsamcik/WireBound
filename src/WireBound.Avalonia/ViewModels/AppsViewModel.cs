using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WireBound.Avalonia.Helpers;
using WireBound.Core;
using WireBound.Core.Helpers;
using WireBound.Core.Models;
using WireBound.Core.Services;

namespace WireBound.Avalonia.ViewModels;

/// <summary>
/// Page-scoped Task Manager-style monitor for currently running processes.
/// The timer is disabled until the Apps route is visible and is stopped as
/// soon as the user navigates away, so process enumeration never becomes an
/// always-on application cost.
/// </summary>
public sealed partial class AppsViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);

    private readonly IUiDispatcher _dispatcher;
    private readonly IProcessUsageService _processUsageService;
    private readonly INavigationService _navigationService;
    private readonly ILogger<AppsViewModel>? _logger;
    private readonly Dictionary<int, ProcessUsageDisplayItem> _itemsByProcessId = [];

    private ITimer? _refreshTimer;
    private CancellationTokenSource? _activationCts;
    private int _activationVersion;
    private int _refreshInProgress;
    private int _refreshQueued;
    private bool _disposed;

    /// <summary>
    /// Completes after the first visible-page capture. It is already complete
    /// for an instance constructed while another route is active.
    /// </summary>
    public Task InitializationTask { get; }

    [ObservableProperty]
    private BatchObservableCollection<ProcessUsageDisplayItem> _processItems = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _showSystemProcesses = true;

    [ObservableProperty]
    private ProcessUsageSortColumn _sortColumn = ProcessUsageSortColumn.Cpu;

    [ObservableProperty]
    private bool _sortDescending = true;

    [ObservableProperty]
    private bool _isPageActive;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _processCount;

    [ObservableProperty]
    private int _visibleProcessCount;

    public bool HasVisibleProcesses => VisibleProcessCount > 0;

    [ObservableProperty]
    private string _totalCpu = "—";

    [ObservableProperty]
    private string _totalMemory = "0 B";

    [ObservableProperty]
    private string _totalDownloadSpeed = "—";

    [ObservableProperty]
    private string _totalUploadSpeed = "—";

    [ObservableProperty]
    private bool _hasNetworkData;

    [ObservableProperty]
    private DateTime? _lastUpdated;

    [ObservableProperty]
    private string? _captureError;

    public string LastUpdatedLabel => !string.IsNullOrWhiteSpace(CaptureError)
        ? CaptureError
        : LastUpdated is { } timestamp
        ? $"Updated {timestamp:HH:mm:ss}"
        : "Waiting for first sample…";

    public string NameSortGlyph => GetSortGlyph(ProcessUsageSortColumn.Name);
    public string CpuSortGlyph => GetSortGlyph(ProcessUsageSortColumn.Cpu);
    public string MemorySortGlyph => GetSortGlyph(ProcessUsageSortColumn.Memory);
    public string DownloadSortGlyph => GetSortGlyph(ProcessUsageSortColumn.Download);
    public string UploadSortGlyph => GetSortGlyph(ProcessUsageSortColumn.Upload);

    public AppsViewModel(
        IUiDispatcher dispatcher,
        IProcessUsageService processUsageService,
        INavigationService navigationService,
        ILogger<AppsViewModel>? logger = null,
        TimeProvider? timeProvider = null)
    {
        _dispatcher = dispatcher;
        _processUsageService = processUsageService;
        _navigationService = navigationService;
        _logger = logger;

        IsPageActive = _navigationService.CurrentView == Routes.Apps;
        _refreshTimer = (timeProvider ?? TimeProvider.System).CreateTimer(
            static state => ((AppsViewModel)state!).OnRefreshTimerTick(),
            this,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);

        _navigationService.NavigationChanged += OnNavigationChanged;
        InitializationTask = IsPageActive ? ActivateAsync() : Task.CompletedTask;
    }

    private void OnNavigationChanged(string route)
    {
        var shouldBeActive = route == Routes.Apps;
        if (shouldBeActive == IsPageActive)
        {
            return;
        }

        IsPageActive = shouldBeActive;
        if (shouldBeActive)
        {
            _ = ActivateAsync();
        }
        else
        {
            Deactivate();
        }
    }

    private Task ActivateAsync()
    {
        if (_disposed || !IsPageActive)
        {
            return Task.CompletedTask;
        }

        CancelActivation();
        _activationCts = new CancellationTokenSource();
        Interlocked.Increment(ref _activationVersion);
        _processUsageService.Reset();
        CaptureError = null;
        IsLoading = ProcessItems.Count == 0;
        _refreshTimer?.Change(RefreshInterval, RefreshInterval);

        // Capture immediately on entry; the periodic timer maintains the view
        // after that. This is still pull-based and no sampling happens before
        // the route is active.
        return RequestRefreshAsync();
    }

    private void Deactivate()
    {
        _refreshTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        Interlocked.Increment(ref _activationVersion);
        Interlocked.Exchange(ref _refreshQueued, 0);
        CancelActivation();
        _processUsageService.Reset();
        IsLoading = false;
    }

    private void CancelActivation()
    {
        var previous = Interlocked.Exchange(ref _activationCts, null);
        if (previous is null)
        {
            return;
        }

        previous.Cancel();
        previous.Dispose();
    }

    private void OnRefreshTimerTick() => _ = RequestRefreshAsync();

    [RelayCommand]
    private Task RefreshAsync() => RequestRefreshAsync();

    private Task RequestRefreshAsync()
    {
        if (_disposed || !IsPageActive)
        {
            return Task.CompletedTask;
        }

        if (Interlocked.CompareExchange(ref _refreshInProgress, 1, 0) != 0)
        {
            // Never overlap process walks. One follow-up capture is enough to
            // coalesce any timer ticks that arrived while the provider was busy.
            Interlocked.Exchange(ref _refreshQueued, 1);
            return Task.CompletedTask;
        }

        return RefreshCoreAsync();
    }

    private async Task RefreshCoreAsync()
    {
        CancellationTokenSource? cts = null;
        var version = 0;

        try
        {
            cts = _activationCts;
            if (cts is null || _disposed || !IsPageActive)
            {
                return;
            }

            version = Volatile.Read(ref _activationVersion);
            var snapshots = await _processUsageService.CaptureAsync(cts.Token).ConfigureAwait(false);

            if (cts.IsCancellationRequested || _disposed || !IsPageActive || version != Volatile.Read(ref _activationVersion))
            {
                return;
            }

            await _dispatcher.InvokeAsync(() =>
            {
                // The capture may have finished just as navigation changed.
                // Recheck inside the UI callback so stale work cannot repaint
                // an invisible page or retain exited-process rows.
                if (cts.IsCancellationRequested || _disposed || !IsPageActive || version != Volatile.Read(ref _activationVersion))
                {
                    return;
                }

                ApplySnapshot(snapshots);
            });
        }
        catch (OperationCanceledException)
        {
            // Expected when navigating away or disposing the singleton VM.
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to capture live process usage");

            if (cts is not null)
            {
                await _dispatcher.InvokeAsync(() =>
                {
                    // A failure that belongs to an old activation must not
                    // overwrite the status of a newly-visible page.
                    if (cts.IsCancellationRequested || _disposed || !IsPageActive || version != Volatile.Read(ref _activationVersion))
                    {
                        return;
                    }

                    IsLoading = false;
                    CaptureError = "Couldn’t refresh process usage. Try again.";
                }).ConfigureAwait(false);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _refreshInProgress, 0);

            if (Interlocked.Exchange(ref _refreshQueued, 0) != 0 && !_disposed && IsPageActive)
            {
                _ = RequestRefreshAsync();
            }
        }
    }

    private void ApplySnapshot(IReadOnlyList<ProcessUsageSnapshot> snapshots)
    {
        var activeProcessIds = new HashSet<int>();
        foreach (var snapshot in snapshots)
        {
            activeProcessIds.Add(snapshot.ProcessId);
            if (!_itemsByProcessId.TryGetValue(snapshot.ProcessId, out var item))
            {
                item = new ProcessUsageDisplayItem(snapshot);
                _itemsByProcessId.Add(snapshot.ProcessId, item);
            }
            else
            {
                item.Update(snapshot);
            }
        }

        foreach (var processId in _itemsByProcessId.Keys.Where(id => !activeProcessIds.Contains(id)).ToArray())
        {
            _itemsByProcessId.Remove(processId);
        }

        ProcessCount = snapshots.Count;
        HasNetworkData = snapshots.Any(snapshot => snapshot.HasNetworkStats);
        var cpuSnapshots = snapshots.Where(snapshot => snapshot.HasCpuSample).ToArray();
        TotalCpu = cpuSnapshots.Length == 0
            ? "Collecting…"
            : $"{cpuSnapshots.Sum(snapshot => snapshot.CpuPercent):F1}%";
        TotalMemory = ByteFormatter.FormatBytes(snapshots.Sum(snapshot => Math.Max(0, snapshot.WorkingSetBytes)));
        TotalDownloadSpeed = HasNetworkData
            ? ByteFormatter.FormatSpeed(snapshots.Sum(snapshot => snapshot.DownloadSpeedBps))
            : "—";
        TotalUploadSpeed = HasNetworkData
            ? ByteFormatter.FormatSpeed(snapshots.Sum(snapshot => snapshot.UploadSpeedBps))
            : "—";
        LastUpdated = DateTime.Now;
        CaptureError = null;
        IsLoading = false;

        RebuildVisibleItems();
    }

    partial void OnSearchTextChanged(string value) => RebuildVisibleItems();
    partial void OnShowSystemProcessesChanged(bool value) => RebuildVisibleItems();
    partial void OnLastUpdatedChanged(DateTime? value) => OnPropertyChanged(nameof(LastUpdatedLabel));
    partial void OnCaptureErrorChanged(string? value) => OnPropertyChanged(nameof(LastUpdatedLabel));
    partial void OnVisibleProcessCountChanged(int value) => OnPropertyChanged(nameof(HasVisibleProcesses));

    [RelayCommand]
    private void ToggleSort(ProcessUsageSortColumn column)
    {
        if (SortColumn == column)
        {
            SortDescending = !SortDescending;
        }
        else
        {
            SortColumn = column;
            SortDescending = column != ProcessUsageSortColumn.Name;
        }

        NotifySortGlyphsChanged();
        RebuildVisibleItems();
    }

    private void RebuildVisibleItems()
    {
        if (_disposed)
        {
            return;
        }

        IEnumerable<ProcessUsageDisplayItem> visible = _itemsByProcessId.Values;
        if (!ShowSystemProcesses)
        {
            visible = visible.Where(item => !item.IsSystemProcess);
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim();
            visible = visible.Where(item => item.Matches(term));
        }

        var sorted = SortItems(visible).ToArray();
        var orderChanged = ProcessItems.Count != sorted.Length
            || ProcessItems.Where((item, index) => !ReferenceEquals(item, sorted[index])).Any();

        // The row objects themselves already raise property changes. Avoiding a
        // collection Reset when the visible membership and order are stable
        // preserves realized virtualized containers and prevents needless layout
        // churn every two-second refresh.
        if (orderChanged)
        {
            ProcessItems.ReplaceAll(sorted);
        }

        VisibleProcessCount = sorted.Length;
    }

    private IEnumerable<ProcessUsageDisplayItem> SortItems(IEnumerable<ProcessUsageDisplayItem> items)
    {
        return SortColumn switch
        {
            ProcessUsageSortColumn.Name => SortDescending
                ? items.OrderByDescending(item => item.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.ProcessId)
                : items.OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.ProcessId),
            ProcessUsageSortColumn.Memory => SortDescending
                ? items.OrderByDescending(item => item.WorkingSetBytes).ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                : items.OrderBy(item => item.WorkingSetBytes).ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase),
            ProcessUsageSortColumn.Download => SortDescending
                ? items.OrderByDescending(item => item.DownloadSpeedBps).ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                : items.OrderBy(item => item.DownloadSpeedBps).ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase),
            ProcessUsageSortColumn.Upload => SortDescending
                ? items.OrderByDescending(item => item.UploadSpeedBps).ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                : items.OrderBy(item => item.UploadSpeedBps).ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase),
            _ => SortCpu(items)
        };
    }

    private IEnumerable<ProcessUsageDisplayItem> SortCpu(IEnumerable<ProcessUsageDisplayItem> items)
    {
        // Baseline-only rows do not yet have a meaningful CPU value. Keep them
        // below rows with real samples regardless of sort direction.
        return SortDescending
            ? items.OrderByDescending(item => item.HasCpuSample)
                .ThenByDescending(item => item.CpuPercent)
                .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            : items.OrderByDescending(item => item.HasCpuSample)
                .ThenBy(item => item.CpuPercent)
                .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase);
    }

    private string GetSortGlyph(ProcessUsageSortColumn column)
    {
        return SortColumn == column ? (SortDescending ? "▼" : "▲") : string.Empty;
    }

    private void NotifySortGlyphsChanged()
    {
        OnPropertyChanged(nameof(NameSortGlyph));
        OnPropertyChanged(nameof(CpuSortGlyph));
        OnPropertyChanged(nameof(MemorySortGlyph));
        OnPropertyChanged(nameof(DownloadSortGlyph));
        OnPropertyChanged(nameof(UploadSortGlyph));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _navigationService.NavigationChanged -= OnNavigationChanged;
        IsPageActive = false;
        Deactivate();
        _refreshTimer?.Dispose();
        _refreshTimer = null;
    }
}

/// <summary>
/// A lightweight row model reused across snapshots so virtualized item
/// containers retain their bindings and only changed cells notify.
/// </summary>
public sealed partial class ProcessUsageDisplayItem : ObservableObject
{
    public int ProcessId { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    [NotifyPropertyChangedFor(nameof(IsSystemProcess))]
    private string _processName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    [NotifyPropertyChangedFor(nameof(IsSystemProcess))]
    private string _executablePath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CpuDisplay))]
    private double _cpuPercent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CpuDisplay))]
    private bool _hasCpuSample;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MemoryDisplay))]
    private long _privateBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MemoryDisplay))]
    private long _workingSetBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DownloadDisplay))]
    private long _downloadSpeedBps;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UploadDisplay))]
    private long _uploadSpeedBps;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SessionTotalDisplay))]
    private long _sessionBytesReceived;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SessionTotalDisplay))]
    private long _sessionBytesSent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DownloadDisplay))]
    [NotifyPropertyChangedFor(nameof(UploadDisplay))]
    [NotifyPropertyChangedFor(nameof(SessionTotalDisplay))]
    private bool _hasNetworkStats;

    public ProcessUsageDisplayItem(ProcessUsageSnapshot snapshot)
    {
        ProcessId = snapshot.ProcessId;
        Update(snapshot);
    }

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ExecutablePath))
            {
                var fileName = Path.GetFileNameWithoutExtension(ExecutablePath);
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    return fileName;
                }
            }

            return string.IsNullOrWhiteSpace(ProcessName) ? "Unknown process" : ProcessName;
        }
    }

    public string ProcessIdLabel => $"PID {ProcessId}";
    public string CpuDisplay => HasCpuSample ? $"{CpuPercent:F1}%" : "—";
    public string MemoryDisplay => ByteFormatter.FormatBytes(Math.Max(0, WorkingSetBytes));
    public string DownloadDisplay => HasNetworkStats ? ByteFormatter.FormatSpeed(DownloadSpeedBps) : "—";
    public string UploadDisplay => HasNetworkStats ? ByteFormatter.FormatSpeed(UploadSpeedBps) : "—";
    public string SessionTotalDisplay => HasNetworkStats
        ? ByteFormatter.FormatBytes(Math.Max(0, SessionBytesReceived) + Math.Max(0, SessionBytesSent))
        : "—";
    public bool IsSystemProcess => string.IsNullOrWhiteSpace(ExecutablePath)
                                   || string.Equals(ProcessName, "System", StringComparison.OrdinalIgnoreCase)
                                   || string.Equals(ProcessName, "Idle", StringComparison.OrdinalIgnoreCase);

    public void Update(ProcessUsageSnapshot snapshot)
    {
        ProcessName = snapshot.ProcessName;
        ExecutablePath = snapshot.ExecutablePath;
        CpuPercent = snapshot.CpuPercent;
        HasCpuSample = snapshot.HasCpuSample;
        PrivateBytes = snapshot.PrivateBytes;
        WorkingSetBytes = snapshot.WorkingSetBytes;
        DownloadSpeedBps = snapshot.DownloadSpeedBps;
        UploadSpeedBps = snapshot.UploadSpeedBps;
        SessionBytesReceived = snapshot.SessionBytesReceived;
        SessionBytesSent = snapshot.SessionBytesSent;
        HasNetworkStats = snapshot.HasNetworkStats;
    }

    public bool Matches(string term)
    {
        return DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
               || ProcessName.Contains(term, StringComparison.OrdinalIgnoreCase)
               || ExecutablePath.Contains(term, StringComparison.OrdinalIgnoreCase)
               || ProcessId.ToString().Contains(term, StringComparison.Ordinal);
    }
}

public enum ProcessUsageSortColumn
{
    Name,
    Cpu,
    Memory,
    Download,
    Upload
}
