using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using WireBound.Avalonia.Helpers;
using WireBound.Avalonia.Services;
using WireBound.Core;
using WireBound.Core.Helpers;
using WireBound.Core.Services;
using WireBound.Platform.Abstract.Models;
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
    private readonly HashSet<string> _expanded = [];
    private string? _selectionToRestore;
    private readonly Dictionary<string, int> _segmentColors = new();
    private bool _disposed;

    [ObservableProperty] private string _inUse = "—";
    [ObservableProperty] private string _available = "—";
    [ObservableProperty] private string _hardwareReserved = "—";
    [ObservableProperty] private string _installed = "—";
    [ObservableProperty] private string _sampleStatus = "Waiting for sample";
    [ObservableProperty] private string _accountingStatus = "Other measurements may overlap; they are not added to physical RAM.";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private MemoryConsumerRow? _selectedConsumer;
    [ObservableProperty] private IReadOnlyList<MemorySegment> _memorySegments = [];
    public BatchObservableCollection<MemoryConsumerRow> Consumers { get; } = new();
    public event Action? ConsumersChanging;
    public event Action? ConsumersChanged;
    public event Action<MemoryConsumerRow>? ConsumerRevealRequested;
    public bool HasSelection => SelectedConsumer is not null;
    public bool HasNoResults => Consumers.Count == 0 && !IsLoading;
    public bool HasMemorySegments => MemorySegments.Count > 0;

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
    partial void OnMemorySegmentsChanged(IReadOnlyList<MemorySegment> value) => OnPropertyChanged(nameof(HasMemorySegments));
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
                Installed = memory.InstalledBytes is > 0 ? ByteFormatter.FormatBytes(memory.InstalledBytes.Value) : "—";
                HardwareReserved = valid && memory.InstalledBytes is { } installed && installed >= memory.TotalBytes
                    ? ByteFormatter.FormatBytes(installed - memory.TotalBytes) : "—";
                _selectionToRestore = SelectedConsumer?.Id;
                _roots = roots;
                MemorySegments = BuildMemorySegments(memory, valid);
                AccountingStatus = roots.Last().PhysicalBytes.HasValue
                    ? "Reported working-set, VM and container values may overlap. The bar shows physical totals."
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
        bool DirectMatch(MemoryConsumerRow row) => row.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || row.Summary.Contains(search, StringComparison.OrdinalIgnoreCase)
            || row.UsageMetric.Contains(search, StringComparison.OrdinalIgnoreCase);
        bool Matches(MemoryConsumerRow row) => DirectMatch(row) || row.Children.Any(Matches);
        var visible = new List<MemoryConsumerRow>();
        void Append(IEnumerable<MemoryConsumerRow> rows, int depth, bool includeAll)
        {
            foreach (var row in SortByUsage(rows))
            {
                if (search.Length > 0 && !includeAll && !Matches(row)) continue;
                row.Depth = depth;
                row.IsFilterExpanded = search.Length > 0 && row.Children.Count > 0;
                visible.Add(row);
                if (row.IsExpanded || search.Length > 0)
                    Append(row.Children, depth + 1, includeAll || (search.Length > 0 && DirectMatch(row)));
            }
        }
        Append(TopLevelConsumers(), 0, false);
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

    private IEnumerable<MemoryConsumerRow> TopLevelConsumers()
    {
        // Keep the physical accounting tree internal; show consumers without category subtotals.
        foreach (var root in _roots)
        {
            if (root.Id == "apps" || root.Id == "system")
            {
                foreach (var child in root.Children) yield return child;
            }
            else if (root.Id == "workloads")
            {
                foreach (var workload in root.Children)
                {
                    // Container totals are subdivisions of the Docker total, not extra consumers.
                    if (workload.Id == "docker" && workload.Children.Count > 0)
                    {
                        foreach (var container in workload.Children) yield return container;
                    }
                    else yield return workload;
                }
            }
            else yield return root;
        }
    }

    private static IOrderedEnumerable<MemoryConsumerRow> SortByUsage(IEnumerable<MemoryConsumerRow> rows) => rows
        .OrderByDescending(row => row.UsageBytes ?? -1)
        .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
        .ThenBy(row => row.Id, StringComparer.Ordinal);
    private static IEnumerable<MemoryConsumerRow> Walk(IEnumerable<MemoryConsumerRow> rows)
    {
        foreach (var row in rows)
        {
            yield return row;
            foreach (var child in Walk(row.Children)) yield return child;
        }
    }

    private IReadOnlyList<MemorySegment> BuildMemorySegments(MemoryInfoData memory, bool valid)
    {
        if (!valid) return [];
        var capacity = memory.InstalledBytes is { } installed && installed >= memory.TotalBytes
            ? installed : memory.TotalBytes;
        var segments = new List<MemorySegment>();
        void Add(string label, long bytes, SKColor color, bool lightText, string? id, string description)
        {
            if (bytes <= 0) return;
            segments.Add(new(label, bytes, bytes * 100d / capacity,
                $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}", lightText, id, description));
        }

        var apps = _roots.Single(r => r.Id == "apps");
        var hosts = _roots.Single(r => r.Id == "workloads");
        var system = _roots.Single(r => r.Id == "system");
        var shared = _roots.Single(r => r.Id == "other");
        if (shared.PhysicalBytes.HasValue)
        {
            // App rows already combine every process from the same executable.
            // Use only their exclusive pages, never working sets or guest/container observations.
            var largest = apps.Children.Concat(hosts.Children)
                .Where(r => r.PhysicalBytes is > 0 && r.PhysicalBytes.Value * 100d / capacity >= 2)
                .OrderByDescending(r => r.PhysicalBytes).ThenBy(r => r.Id, StringComparer.Ordinal)
                .Take(6).ToArray();
            var assigned = new HashSet<int>();
            var retained = largest.Select(r => r.Id).ToHashSet();
            foreach (var id in _segmentColors.Keys.Where(id => !retained.Contains(id)).ToArray()) _segmentColors.Remove(id);
            // Reserve existing colors first so a change in ranking keeps app identities stable.
            foreach (var row in largest)
                if (_segmentColors.TryGetValue(row.Id, out var index)) assigned.Add(index);
            foreach (var row in largest)
            {
                if (!_segmentColors.TryGetValue(row.Id, out var index))
                {
                    index = Enumerable.Range(0, ChartColors.SeriesPalette.Length).First(i => !assigned.Contains(i));
                    _segmentColors[row.Id] = index;
                    assigned.Add(index);
                }
                Add(row.Name, row.PhysicalBytes!.Value, ChartColors.SeriesPalette[index], index == 5,
                    row.Id, $"Measured private RAM · {row.Summary}" + (row.IsPartial ? ". Some processes could not be measured." : "."));
            }
            var smallerApps = apps.Children.Where(r => r.PhysicalBytes is > 0 && !retained.Contains(r.Id)).ToArray();
            var smallerHosts = hosts.Children.Where(r => r.PhysicalBytes is > 0 && !retained.Contains(r.Id)).ToArray();
            Add("Other apps", smallerApps.Sum(r => r.PhysicalBytes!.Value),
                ChartColors.MemoryDimColor, false, SortByUsage(smallerApps).FirstOrDefault()?.Id,
                "Measured private RAM of smaller application groups. Click to reveal the largest of these apps.");
            Add("Other VM hosts", smallerHosts.Sum(r => r.PhysicalBytes!.Value),
                ChartColors.CpuDimColor, false, SortByUsage(smallerHosts).FirstOrDefault()?.Id,
                "Measured private RAM of smaller VM host processes. Click to reveal the largest of these hosts. Guest and container observations are excluded.");
            Add("System", system.PhysicalBytes ?? 0, ChartColors.AxisLabelColor, false, system.Children.FirstOrDefault()?.Id,
                "Measured resident kernel allocations.");
            Add("Shared / other", shared.PhysicalBytes.Value, ChartColors.AxisNameColor, false, shared.Id,
                "Shared pages and RAM without a measured exclusive owner, including unmeasured guest allocations.");
        }
        else
            Add("In use", memory.UsedBytes, ChartColors.MemoryColor, false, shared.Id,
                "Physical attribution is unavailable for this sample. Click for details, or refresh to retry.");

        Add("Available", memory.AvailableBytes, ChartColors.ChartBackgroundColor, true, null,
            "RAM available for applications, including reclaimable cache.");
        Add("Reserved", capacity - memory.TotalBytes, ChartColors.AxisLabelColor, false, null,
            "Installed RAM reserved by hardware; unavailable to the operating system.");
        return segments;
    }

    [RelayCommand]
    private void RevealMemoryConsumer(MemorySegment? segment)
    {
        if (segment?.ConsumerId is not { } id) return;
        var target = Walk(_roots).FirstOrDefault(r => r.Id == id);
        if (target is null) return;
        SearchText = "";
        bool ExpandPath(MemoryConsumerRow row)
        {
            if (!ReferenceEquals(row, target) && !row.Children.Any(ExpandPath)) return false;
            if (row.CanExpand)
            {
                row.IsExpanded = true;
                _expanded.Add(row.Id);
            }
            return true;
        }
        foreach (var root in _roots) ExpandPath(root);
        RebuildVisible();
        SelectedConsumer = target;
        ConsumerRevealRequested?.Invoke(target);
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
