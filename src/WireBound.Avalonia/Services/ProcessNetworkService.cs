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
    private IReadOnlyList<ProcessNetworkStats> _statsSnapshot = [];
    private readonly SemaphoreSlim _providerLock = new(1, 1);
    private readonly object _statsLock = new();
    private bool _disposed;

    public bool IsRunning => _currentProvider is { IsMonitoring: true };
    public bool HasRequiredPrivileges => _providerFactory.HasElevatedProvider;
    public bool IsPlatformSupported { get; } = true;

    /// <summary>Exposes the last provider-change task for testability.</summary>
    internal Task? PendingProviderChangeTask { get; private set; }

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
            await _providerLock.WaitAsync().ConfigureAwait(false);
            try
            {
                _currentProvider = _providerFactory.GetProvider();
                _currentProvider.StatsUpdated += OnProviderStatsUpdated;
                _currentProvider.ErrorOccurred += OnProviderErrorOccurred;
            }
            finally
            {
                _providerLock.Release();
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
        return _statsSnapshot;
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
        await _providerLock.WaitAsync().ConfigureAwait(false);
        IProcessNetworkProvider provider;
        try
        {
            if (_currentProvider == null)
                _currentProvider = _providerFactory.GetProvider();
            provider = _currentProvider;
        }
        finally
        {
            _providerLock.Release();
        }

        return await provider.GetConnectionStatsAsync();
    }

    private void OnProviderStatsUpdated(object? sender, ProcessNetworkProviderEventArgs e)
    {
        IReadOnlyList<ProcessNetworkStats> snapshot;
        lock (_statsLock)
        {
            _currentStats.Clear();
            _currentStats.AddRange(e.Stats);
            snapshot = _currentStats.ToList();
        }

        // Publish immutable snapshot for lock-free reads
        _statsSnapshot = snapshot;
        StatsUpdated?.Invoke(this, new ProcessStatsUpdatedEventArgs(e.Stats.ToList()));
    }

    private void OnProviderErrorOccurred(object? sender, ProcessNetworkProviderErrorEventArgs e)
    {
        ErrorOccurred?.Invoke(this, new ProcessNetworkErrorEventArgs(
            e.Message,
            e.Exception,
            requiresElevation: false));
    }

    private void OnProviderChanged(object? sender, ProviderChangedEventArgs e)
    {
        if (_disposed) return;
        var task = HandleProviderChangedAsync(e);
        PendingProviderChangeTask = task;
    }

    private async Task HandleProviderChangedAsync(ProviderChangedEventArgs e)
    {
        try
        {
            IProcessNetworkProvider? oldProvider;
            bool wasMonitoring;

            await _providerLock.WaitAsync().ConfigureAwait(false);
            try
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
            finally
            {
                _providerLock.Release();
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
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error handling provider change");
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
