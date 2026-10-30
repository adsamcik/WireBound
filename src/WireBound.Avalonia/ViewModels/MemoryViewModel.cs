using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WireBound.Avalonia.Helpers;
using WireBound.Avalonia.Services;
using WireBound.Core;
using WireBound.Core.Helpers;
using WireBound.Core.Services;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Avalonia.ViewModels;

public sealed partial class MemoryViewModel : ObservableObject, IDisposable
{
    private readonly IMemoryInfoProvider _memoryProvider;
    private readonly IProcessUsageService _processUsage;
    private readonly IWorkloadHostProvider _hosts;
    private readonly IWorkloadMemoryService _workloads;
    private readonly IUiDispatcher _dispatcher;
    private readonly INavigationService _navigation;
    private readonly ProcessContextService _context;
    private CancellationTokenSource? _capture;
    private IReadOnlyList<MemoryConsumerRow> _roots = [];
    private readonly HashSet<string> _expanded = ["apps", "workloads"];
    private string? _selectionToRestore;
    private bool _disposed;

    [ObservableProperty] private string _inUse = "—";
    [ObservableProperty] private string _available = "—";
    [ObservableProperty] private string _hardwareReserved = "—";
    [ObservableProperty] private string _installed = "—";
    [ObservableProperty] private double _usedPercent;
    [ObservableProperty] private string _sampleStatus = "Waiting for sample";
    [ObservableProperty] private string _accountingStatus = "Other measurements may overlap; they are not added to physical RAM.";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private MemoryConsumerRow? _selectedConsumer;
    public BatchObservableCollection<MemoryConsumerRow> Consumers { get; } = new();
    public event Action? ConsumersChanging;
    public event Action? ConsumersChanged;
    public bool HasSelection => SelectedConsumer is not null;
    public bool HasNoResults => Consumers.Count == 0 && !IsLoading;

    public MemoryViewModel(IMemoryInfoProvider memoryProvider, IProcessUsageService processUsage,
        IWorkloadHostProvider hosts, IUiDispatcher dispatcher, INavigationService navigation,
        ProcessContextService context, IWorkloadMemoryService workloads)
    {
        _memoryProvider = memoryProvider;
        _processUsage = processUsage;
        _hosts = hosts;
        _dispatcher = dispatcher;
        _navigation = navigation;
        _context = context;
        _workloads = workloads;
        _navigation.NavigationChanged += OnNavigationChanged;
        if (_navigation.CurrentView == Routes.Memory) _ = RefreshAsync();
    }
    partial void OnSelectedConsumerChanged(MemoryConsumerRow? value)
    {
        if (value is not null) _selectionToRestore = null;
        OnPropertyChanged(nameof(HasSelection));
    }
    partial void OnSearchTextChanged(string value) => RebuildVisible();
    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(HasNoResults));
    private void OnNavigationChanged(string route)
    {
        if (route == Routes.Memory) _ = RefreshAsync();
        else
        {
            _capture?.Cancel();
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_disposed || _navigation.CurrentView != Routes.Memory) return;
        _capture?.Cancel();
        _capture?.Dispose();
        var capture = new CancellationTokenSource();
        _capture = capture;
        IsLoading = true;
        SampleStatus = _roots.Count > 0 ? "Refreshing · previous sample shown" : "Reading memory…";
        try
        {
            var memoryTask = Task.Run(() => (_memoryProvider.GetMemoryInfo(), DateTime.Now), capture.Token);
            var processesTask = _processUsage.CaptureAsync(capture.Token, includePrivateWorkingSet: true);
            var workloadTask = _workloads.CaptureAsync(capture.Token);
            await Task.WhenAll(memoryTask, processesTask, workloadTask).ConfigureAwait(false);
            var (memory, capturedAt) = await memoryTask;
            var processes = await processesTask;
            var workloads = await workloadTask;
            var valid = memory.TotalBytes > 0 && memory.AvailableBytes >= 0
                && memory.UsedBytes >= 0 && memory.UsedBytes <= memory.TotalBytes
                && memory.AvailableBytes == memory.TotalBytes - memory.UsedBytes;
            var roots = MemoryConsumerBuilder.Build(memory, processes, workloads, _hosts, valid);
            await _dispatcher.InvokeAsync(() =>
            {
                if (capture.IsCancellationRequested || _disposed || _navigation.CurrentView != Routes.Memory) return;
                InUse = valid ? ByteFormatter.FormatBytes(memory.UsedBytes) : "—";
                Available = valid ? ByteFormatter.FormatBytes(memory.AvailableBytes) : "—";
                UsedPercent = valid ? memory.UsedBytes * 100d / memory.TotalBytes : 0;
                Installed = memory.InstalledBytes is > 0 ? ByteFormatter.FormatBytes(memory.InstalledBytes.Value) : "—";
                HardwareReserved = valid && memory.InstalledBytes is { } installed && installed >= memory.TotalBytes
                    ? ByteFormatter.FormatBytes(installed - memory.TotalBytes) : "—";
                _selectionToRestore = SelectedConsumer?.Id;
                _roots = roots;
                AccountingStatus = roots.Last().PhysicalBytes.HasValue
                    ? "Apps + VM hosts + system + shared and other = in use. Other measurements may overlap; ≥ is partial."
                    : "Physical attribution unavailable. Refresh to retry. Other measurements may overlap.";
                RestoreExpansion(_roots);
                RebuildVisible();
                SampleStatus = valid ? $"Sampled {capturedAt:HH:mm:ss}" : "Physical RAM unavailable";
                IsLoading = false;
                foreach (var container in Walk(_roots).Where(r => r.ContainerId is not null && _expanded.Contains(r.Id)).ToArray())
                    _ = ToggleConsumerAsync(container);
            });
        }
        catch (OperationCanceledException) { }
        catch
        {
            await _dispatcher.InvokeAsync(() =>
            {
                if (capture.IsCancellationRequested || _disposed) return;
                SampleStatus = _roots.Count > 0 ? "Refresh failed · previous sample shown. Retry Refresh." : "Memory unavailable. Retry Refresh.";
                IsLoading = false;
            });
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ToggleConsumerAsync(MemoryConsumerRow? row)
    {
        if (row is null || !row.CanToggle || _disposed) return;
        row.IsExpanded = !row.IsExpanded;
        if (row.IsExpanded) _expanded.Add(row.Id);
        else _expanded.Remove(row.Id);
        RebuildVisible();
        if (!row.IsExpanded || row.ContainerId is null || row.ChildrenLoaded || row.IsLoading) return;
        var capture = _capture;
        if (capture is null || capture.IsCancellationRequested) return;
        row.IsLoading = true;
        try
        {
            var children = await _workloads.GetContainerProcessesAsync(row.ContainerId, capture.Token).ConfigureAwait(false);
            await _dispatcher.InvokeAsync(() =>
            {
                if (capture.IsCancellationRequested || _disposed || !Walk(_roots).Contains(row)) return;
                row.Children.Clear();
                row.Children.AddRange(children.Select(MemoryConsumerBuilder.FromWorkload));
                row.ChildrenLoaded = true;
                RebuildVisible();
            });
        }
        catch (OperationCanceledException) { }
        catch
        {
            await _dispatcher.InvokeAsync(() =>
            {
                if (capture.IsCancellationRequested || _disposed) return;
                row.Children.Clear();
                row.Children.Add(new MemoryConsumerRow { Id = row.Id + ":error", Name = "Processes unavailable", Detail = "Collapse and expand to retry.", Metric = "Unavailable" });
                RebuildVisible();
            });
        }
        finally { await _dispatcher.InvokeAsync(() => row.IsLoading = false); }
    }
    private void RestoreExpansion(IEnumerable<MemoryConsumerRow> rows)
    {
        foreach (var row in rows)
        {
            row.IsExpanded = _expanded.Contains(row.Id) && row.ContainerId is null;
            RestoreExpansion(row.Children);
        }
    }
    private void RebuildVisible()
    {
        var selected = SelectedConsumer;
        var search = SearchText.Trim();
        bool Matches(MemoryConsumerRow row) => row.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || row.Summary.Contains(search, StringComparison.OrdinalIgnoreCase) || row.Children.Any(Matches);
        var visible = new List<MemoryConsumerRow>();
        void Append(IEnumerable<MemoryConsumerRow> rows, int depth, bool includeAll)
        {
            foreach (var row in rows)
            {
                if (search.Length > 0 && !includeAll && !Matches(row)) continue;
                row.Depth = depth;
                row.IsFilterExpanded = search.Length > 0 && row.Children.Count > 0;
                visible.Add(row);
                if (row.IsExpanded || search.Length > 0)
                    Append(row.Children, depth + 1, includeAll || (search.Length > 0 && row.Name.Contains(search, StringComparison.OrdinalIgnoreCase)));
            }
        }
        Append(_roots, 0, false);
        ConsumersChanging?.Invoke();
        try
        {
            Consumers.ReplaceAll(visible);
            SelectedConsumer = selected is not null && visible.Contains(selected) ? selected
                : visible.FirstOrDefault(r => r.Id == _selectionToRestore);
        }
        finally { ConsumersChanged?.Invoke(); }
        OnPropertyChanged(nameof(HasNoResults));
    }
    private static IEnumerable<MemoryConsumerRow> Walk(IEnumerable<MemoryConsumerRow> rows)
    {
        foreach (var row in rows)
        {
            yield return row;
            foreach (var child in Walk(row.Children)) yield return child;
        }
    }
    [RelayCommand]
    private void ViewRelatedProcesses()
    {
        if (SelectedConsumer is not { CanLink: true } row) return;
        _context.Set(new ProcessContext(row.Id, row.Name, row.Detail, DateTime.Now,
            row.HasConfirmedProcessLinks ? new HashSet<ProcessInstanceId>() : row.Processes,
            row.HasConfirmedProcessLinks ? row.Processes : null));
        _navigation.NavigateTo(Routes.Apps);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _navigation.NavigationChanged -= OnNavigationChanged;
        _capture?.Cancel();
        _capture?.Dispose();
    }
}
