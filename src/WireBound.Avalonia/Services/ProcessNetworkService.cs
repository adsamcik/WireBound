using Microsoft.Extensions.Logging;
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
    private readonly ILogger<ProcessNetworkService>? _logger;
    private IProcessNetworkProvider? _currentProvider;
    private readonly List<ProcessNetworkStats> _currentStats = [];
    private readonly object _providerLock = new();
    private bool _disposed;

    public bool IsRunning => _currentProvider is { IsMonitoring: true };
    public bool HasRequiredPrivileges => _providerFactory.HasElevatedProvider;
    public bool IsPlatformSupported { get; } = true;

    public event EventHandler<ProcessStatsUpdatedEventArgs>? StatsUpdated;
    public event EventHandler<ProcessNetworkErrorEventArgs>? ErrorOccurred;

    public ProcessNetworkService(
        IProcessNetworkProviderFactory providerFactory,
        ILogger<ProcessNetworkService>? logger = null)
    {
        _providerFactory = providerFactory;
        _logger = logger;
        _providerFactory.ProviderChanged += OnProviderChanged;
    }

    public async Task<bool> StartAsync()
    {
        if (_disposed) return false;
        if (_currentProvider?.IsMonitoring == true) return true;

        try
        {
            lock (_providerLock)
            {
                _currentProvider = _providerFactory.GetProvider();
                _currentProvider.StatsUpdated += OnProviderStatsUpdated;
                _currentProvider.ErrorOccurred += OnProviderErrorOccurred;
            }

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
        lock (_providerLock)
        {
            return _currentStats.ToList();
        }
    }

    public IReadOnlyList<ProcessNetworkStats> GetTopProcesses(int count)
    {
        lock (_providerLock)
        {
            return _currentStats
                .OrderByDescending(s => s.TotalSpeedBps)
                .Take(count)
                .ToList();
        }
    }

    public async Task<IReadOnlyList<Platform.Abstract.Models.ConnectionStats>> GetConnectionStatsAsync()
    {
        IProcessNetworkProvider provider;
        lock (_providerLock)
        {
            if (_currentProvider == null)
                _currentProvider = _providerFactory.GetProvider();
            provider = _currentProvider;
        }

        return await provider.GetConnectionStatsAsync();
    }

    private void OnProviderStatsUpdated(object? sender, ProcessNetworkProviderEventArgs e)
    {
        lock (_providerLock)
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

    private async void OnProviderChanged(object? sender, ProviderChangedEventArgs e)
    {
        if (_disposed) return;

        IProcessNetworkProvider? oldProvider;
        bool wasMonitoring;

        lock (_providerLock)
        {
            oldProvider = _currentProvider;
            wasMonitoring = oldProvider is { IsMonitoring: true };

            if (oldProvider != null)
            {
                oldProvider.StatsUpdated -= OnProviderStatsUpdated;
                oldProvider.ErrorOccurred -= OnProviderErrorOccurred;
            }

            _currentProvider = e.NewProvider;
            _currentProvider.StatsUpdated += OnProviderStatsUpdated;
            _currentProvider.ErrorOccurred += OnProviderErrorOccurred;
        }

        if (oldProvider != null)
        {
            try { await oldProvider.StopMonitoringAsync(); }
            catch (Exception ex) { _logger?.LogDebug(ex, "Best-effort stop of old provider failed"); }
        }

        if (wasMonitoring)
        {
            try { await _currentProvider.StartMonitoringAsync(); }
            catch (Exception ex) { _logger?.LogWarning(ex, "Failed to start new provider after provider change"); }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _providerFactory.ProviderChanged -= OnProviderChanged;

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
