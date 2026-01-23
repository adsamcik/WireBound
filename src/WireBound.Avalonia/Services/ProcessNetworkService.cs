using WireBound.Core.Models;
using WireBound.Core.Services;
using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Avalonia.Services;

/// <summary>
/// Implementation of IProcessNetworkService that adapts the platform-specific IProcessNetworkProvider
/// to the application-level service interface.
/// </summary>
public sealed class ProcessNetworkService : IProcessNetworkService
{
    private readonly IProcessNetworkProviderFactory _providerFactory;
    private IProcessNetworkProvider? _currentProvider;
    private readonly List<ProcessNetworkStats> _currentStats = [];
    private readonly object _statsLock = new();
    private bool _disposed;

    public bool IsRunning => _currentProvider is { IsMonitoring: true };
    public bool HasRequiredPrivileges => _providerFactory.HasElevatedProvider;
    public bool IsPlatformSupported { get; } = true;

    public event EventHandler<ProcessStatsUpdatedEventArgs>? StatsUpdated;
    public event EventHandler<ProcessNetworkErrorEventArgs>? ErrorOccurred;

    public ProcessNetworkService(IProcessNetworkProviderFactory providerFactory)
    {
        _providerFactory = providerFactory;
    }

    public async Task<bool> StartAsync()
    {
        if (_disposed) return false;
        if (_currentProvider?.IsMonitoring == true) return true;

        try
        {
            _currentProvider = _providerFactory.GetProvider();
            _currentProvider.StatsUpdated += OnProviderStatsUpdated;
            _currentProvider.ErrorOccurred += OnProviderErrorOccurred;

            await _currentProvider.StartMonitoringAsync();
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ProcessNetworkErrorEventArgs(
                "Failed to start process network monitoring",
                ex,
                requiresElevation: false));
            return false;
        }
    }

    public async Task StopAsync()
    {
        if (_currentProvider == null) return;

        try
        {
            await _currentProvider.StopMonitoringAsync();
        }
        finally
        {
            if (_currentProvider != null)
            {
                _currentProvider.StatsUpdated -= OnProviderStatsUpdated;
                _currentProvider.ErrorOccurred -= OnProviderErrorOccurred;
            }
        }
    }

    public IReadOnlyList<ProcessNetworkStats> GetCurrentStats()
    {
        lock (_statsLock)
        {
            return _currentStats.ToList();
        }
    }

    public IReadOnlyList<ProcessNetworkStats> GetTopProcesses(int count)
    {
        lock (_statsLock)
        {
            return _currentStats
                .OrderByDescending(s => s.TotalSpeedBps)
                .Take(count)
                .ToList();
        }
    }

    public async Task<IReadOnlyList<Platform.Abstract.Models.ConnectionStats>> GetConnectionStatsAsync()
    {
        if (_currentProvider == null)
        {
            _currentProvider = _providerFactory.GetProvider();
        }
        
        return await _currentProvider.GetConnectionStatsAsync();
    }

    private void OnProviderStatsUpdated(object? sender, ProcessNetworkProviderEventArgs e)
    {
        lock (_statsLock)
        {
            _currentStats.Clear();
            _currentStats.AddRange(e.Stats);
        }

        StatsUpdated?.Invoke(this, new ProcessStatsUpdatedEventArgs(e.Stats.ToList()));
    }

    private void OnProviderErrorOccurred(object? sender, ProcessNetworkProviderErrorEventArgs e)
    {
        ErrorOccurred?.Invoke(this, new ProcessNetworkErrorEventArgs(
            e.Message,
            e.Exception,
            requiresElevation: false));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_currentProvider != null)
        {
            _currentProvider.StatsUpdated -= OnProviderStatsUpdated;
            _currentProvider.ErrorOccurred -= OnProviderErrorOccurred;

            if (_currentProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
