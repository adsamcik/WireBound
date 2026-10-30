using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WireBound.Avalonia.Helpers;
using WireBound.Avalonia.Services;
using WireBound.Core;
using WireBound.Core.Helpers;
using WireBound.Core.Models;
using WireBound.Core.Services;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Avalonia.ViewModels;

/// <summary>Physical capacity and explicitly non-additive host observations.</summary>
public sealed partial class MemoryViewModel : ObservableObject, IDisposable
{
    private readonly IMemoryInfoProvider _memoryProvider;
    private readonly IProcessUsageService _processUsage;
    private readonly IWorkloadHostProvider _hosts;
    private readonly IUiDispatcher _dispatcher;
    private readonly INavigationService _navigation;
    private readonly ProcessContextService _context;
    private CancellationTokenSource? _capture;
    private bool _disposed;

    [ObservableProperty] private string _inUse = "—";
    [ObservableProperty] private string _available = "—";
    [ObservableProperty] private string _hardwareReserved = "—";
    [ObservableProperty] private string _installed = "—";
    [ObservableProperty] private string _unattributed = "—";
    [ObservableProperty] private string _nonpagedPool = "—";
    [ObservableProperty] private bool _hasNonpagedPool;
    [ObservableProperty] private string _sampleStatus = "Waiting for sample";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private MemoryWorkloadItem? _selectedWorkload;
    [ObservableProperty] private BatchObservableCollection<MemoryWorkloadItem> _observedWorkloads = new();

    public bool HasSelectedWorkload => SelectedWorkload is not null;
    public bool HasObservedWorkloads => ObservedWorkloads.Count > 0;

    public MemoryViewModel(
        IMemoryInfoProvider memoryProvider,
        IProcessUsageService processUsage,
        IWorkloadHostProvider hosts,
        IUiDispatcher dispatcher,
        INavigationService navigation,
        ProcessContextService context)
    {
        _memoryProvider = memoryProvider;
        _processUsage = processUsage;
        _hosts = hosts;
        _dispatcher = dispatcher;
        _navigation = navigation;
        _context = context;
        _navigation.NavigationChanged += OnNavigationChanged;
        if (_navigation.CurrentView == Routes.Memory) _ = RefreshAsync();
    }

    partial void OnSelectedWorkloadChanged(MemoryWorkloadItem? value) => OnPropertyChanged(nameof(HasSelectedWorkload));

    private void OnNavigationChanged(string route)
    {
        if (route == Routes.Memory) _ = RefreshAsync();
        else _capture?.Cancel();
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
        try
        {
            var (memory, memoryCapturedAt) = await Task.Run(() =>
            {
                var value = _memoryProvider.GetMemoryInfo();
                return (value, DateTime.Now);
            }, capture.Token).ConfigureAwait(false);
            var processes = await _processUsage.CaptureAsync(capture.Token).ConfigureAwait(false);
            var observed = processes
                .Select(process => (process, host: _hosts.IdentifyHost(process.ProcessName, process.ExecutablePath)))
                .Where(pair => pair.host is not null)
                .Select(pair => new MemoryWorkloadItem(pair.host!.Kind, pair.host.Label,
                    pair.host.Evidence, pair.process.ProcessId, pair.process.StartMarker,
                    pair.process.WorkingSetBytes))
                .ToArray();
            await _dispatcher.InvokeAsync(() =>
            {
                if (capture.IsCancellationRequested || _disposed || _navigation.CurrentView != Routes.Memory) return;
                Apply(memory, observed, memoryCapturedAt);
            });
        }
        catch (OperationCanceledException) { }
        catch
        {
            await _dispatcher.InvokeAsync(() =>
            {
                if (capture.IsCancellationRequested || _disposed) return;
                SampleStatus = "Sample unavailable";
                IsLoading = false;
            });
        }
    }

    private void Apply(WireBound.Platform.Abstract.Models.MemoryInfoData memory,
        IReadOnlyList<MemoryWorkloadItem> observed, DateTime capturedAt)
    {
        var valid = memory.TotalBytes > 0 && memory.AvailableBytes >= 0
            && memory.UsedBytes >= 0 && memory.UsedBytes <= memory.TotalBytes
            && memory.AvailableBytes == memory.TotalBytes - memory.UsedBytes;
        InUse = valid ? ByteFormatter.FormatBytes(memory.UsedBytes) : "—";
        Available = valid ? ByteFormatter.FormatBytes(memory.AvailableBytes) : "—";
        var pool = valid && memory.NonpagedPoolBytes is { } measuredPool
            && measuredPool >= 0 && measuredPool <= memory.UsedBytes
            ? measuredPool : 0;
        HasNonpagedPool = pool > 0;
        NonpagedPool = HasNonpagedPool ? ByteFormatter.FormatBytes(pool) : "—";
        Unattributed = valid ? ByteFormatter.FormatBytes(memory.UsedBytes - pool) : "—";
        Installed = memory.InstalledBytes is > 0 ? ByteFormatter.FormatBytes(memory.InstalledBytes.Value) : "—";
        HardwareReserved = valid && memory.InstalledBytes is { } installed && installed >= memory.TotalBytes
            ? ByteFormatter.FormatBytes(installed - memory.TotalBytes) : "—";
        var selectedId = SelectedWorkload?.WorkloadId;
        ObservedWorkloads.ReplaceAll(observed);
        SelectedWorkload = observed.FirstOrDefault(item => item.WorkloadId == selectedId);
        OnPropertyChanged(nameof(HasObservedWorkloads));
        SampleStatus = valid ? $"Sampled {capturedAt:HH:mm:ss}" : "Physical RAM unavailable";
        IsLoading = false;
    }

    [RelayCommand]
    private void ViewRelatedProcesses()
    {
        var selected = SelectedWorkload;
        if (selected is not { CanLink: true }) return;
        var instance = new ProcessInstanceId(selected.ProcessId, selected.StartMarker);
        _context.Set(new ProcessContext(selected.WorkloadId, selected.Title,
            selected.Evidence, DateTime.Now, new HashSet<ProcessInstanceId> { instance }));
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

public sealed record MemoryWorkloadItem(string Kind, string Label, string Evidence,
    int ProcessId, long StartMarker, long WorkingSetBytes)
{
    public string WorkloadId => $"{Kind}:{ProcessId}:{StartMarker}";
    public bool CanLink => StartMarker != 0;
    public string Title => $"{Label} · PID {ProcessId}";
    public string ResidentDisplay => $"{ByteFormatter.FormatBytes(Math.Max(0, WorkingSetBytes))} resident";
    public string Detail => $"{Evidence} · Host-process working set; may include shared pages";
}
