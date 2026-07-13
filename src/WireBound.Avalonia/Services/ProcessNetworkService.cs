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
    private volatile IProcessNetworkProvider? _currentProvider;
    private readonly List<ProcessNetworkStats> _currentStats = [];
    private volatile IReadOnlyList<ProcessNetworkStats> _statsSnapshot = [];
    private readonly SemaphoreSlim _providerLock = new(1, 1);
    private readonly object _statsLock = new();
    private volatile bool _disposed;
    private Task? _pendingProviderChangeTask;

    public bool IsRunning => _currentProvider is { IsMonitoring: true };
    public bool HasRequiredPrivileges => _providerFactory.HasElevatedProvider;
    public bool IsPlatformSupported { get; } = true;

    /// <summary>Exposes the last provider-change task for testability.</summary>
    internal Task? PendingProviderChangeTask => Volatile.Read(ref _pendingProviderChangeTask);

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

        IProcessNetworkProvider provider;

        try
        {
            await _providerLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_disposed) return false;
                if (_currentProvider?.IsMonitoring == true) return true;

                provider = _providerFactory.GetProvider();
                provider.StatsUpdated += OnProviderStatsUpdated;
                provider.ErrorOccurred += OnProviderErrorOccurred;
                _currentProvider = provider;
            }
            finally
            {
                _providerLock.Release();
            }

            await provider.StartMonitoringAsync().ConfigureAwait(false);
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
        await _providerLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var provider = _currentProvider;
            if (provider is null) return;

            provider.StatsUpdated -= OnProviderStatsUpdated;
            provider.ErrorOccurred -= OnProviderErrorOccurred;
            _currentProvider = null;

            await provider.StopMonitoringAsync().ConfigureAwait(false);
        }
        finally
        {
            _providerLock.Release();
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

        var stats = await provider.GetConnectionStatsAsync();

        // When the elevated helper is active, recover the owner of any connection it
        // left "Unattributed" (ProcessId 0) using the OS connection table.
        if (_providerFactory.HasElevatedProvider && stats.Count > 0)
        {
            var hasUnattributed = false;
            foreach (var s in stats)
            {
                if (s.ProcessId == 0) { hasUnattributed = true; break; }
            }

            if (hasUnattributed)
            {
                var basic = _providerFactory.GetBasicProvider();
                if (basic != null && !ReferenceEquals(basic, provider))
                    stats = await EnrichUnattributedConnectionsAsync(stats, basic).ConfigureAwait(false);
            }
        }

        return stats;
    }

    /// <summary>
    /// Connections that pre-date the elevated helper's ETW tracking come back with
    /// ProcessId 0 ("Unattributed") because the helper never saw their process-start
    /// event. The OS connection table still knows the owner, so fill those gaps from
    /// the basic (unelevated) provider. Byte counters are cleared on enriched rows:
    /// the OS table has no byte data, and the helper's aggregate-on-first-row total
    /// must not be falsely pinned to a single recovered app.
    /// </summary>
    private async Task<IReadOnlyList<Platform.Abstract.Models.ConnectionStats>> EnrichUnattributedConnectionsAsync(
        IReadOnlyList<Platform.Abstract.Models.ConnectionStats> stats,
        IProcessNetworkProvider basicProvider)
    {
        try
        {
            var osConnections = await basicProvider.GetConnectionStatsAsync().ConfigureAwait(false);
            if (osConnections.Count == 0) return stats;

            // Map connection tuple -> owner, skipping any key that resolves to more
            // than one PID (ambiguous) so we never guess wrong.
            var ownerByKey = new Dictionary<string, (int Pid, string Name)>(StringComparer.OrdinalIgnoreCase);
            var ambiguousKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in osConnections)
            {
                if (c.ProcessId == 0) continue;
                var key = c.ConnectionKey;
                if (ownerByKey.TryGetValue(key, out var existing))
                {
                    if (existing.Pid != c.ProcessId) ambiguousKeys.Add(key);
                }
                else
                {
                    ownerByKey[key] = (c.ProcessId, c.ProcessName);
                }
            }

            foreach (var s in stats)
            {
                if (s.ProcessId != 0) continue;
                var key = s.ConnectionKey;
                if (ambiguousKeys.Contains(key)) continue;
                if (ownerByKey.TryGetValue(key, out var owner))
                {
                    s.ProcessId = owner.Pid;
                    s.ProcessName = owner.Name;
                    // Identity recovered, but per-connection byte volume is unknown.
                    s.BytesSent = 0;
                    s.BytesReceived = 0;
                    s.SendSpeedBps = 0;
                    s.ReceiveSpeedBps = 0;
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to attribute pre-existing connections from the OS connection table");
        }

        return stats;
    }

    private void OnProviderStatsUpdated(object? sender, ProcessNetworkProviderEventArgs e)
    {
        IReadOnlyList<ProcessNetworkStats> snapshot;
        lock (_statsLock)
        {
            snapshot = e.Stats.ToList();
            _currentStats.Clear();
            _currentStats.AddRange(snapshot);
        }

        // Publish immutable snapshot for lock-free reads
        _statsSnapshot = snapshot;
        StatsUpdated?.Invoke(this, new ProcessStatsUpdatedEventArgs(snapshot));
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
        // Publish atomically so tests and disposal always observe the latest queued change.
        Interlocked.Exchange(ref _pendingProviderChangeTask, task);
    }

    private async Task HandleProviderChangedAsync(ProviderChangedEventArgs e)
    {
        try
        {
            await _providerLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_disposed) return;

                var oldProvider = _currentProvider;
                var wasMonitoring = oldProvider is { IsMonitoring: true };

                if (oldProvider != null)
                {
                    oldProvider.StatsUpdated -= OnProviderStatsUpdated;
                    oldProvider.ErrorOccurred -= OnProviderErrorOccurred;
                }

                var newProvider = e.NewProvider;
                newProvider.StatsUpdated += OnProviderStatsUpdated;
                newProvider.ErrorOccurred += OnProviderErrorOccurred;
                _currentProvider = newProvider;

                // Hold _providerLock across Stop/Start so concurrent ProviderChanged
                // events queue instead of swapping _currentProvider underneath us.
                if (oldProvider != null)
                {
                    try { await oldProvider.StopMonitoringAsync().ConfigureAwait(false); }
                    catch (Exception ex) { _logger?.LogDebug(ex, "Best-effort stop of old provider failed"); }
                }

                if (wasMonitoring)
                {
                    try { await newProvider.StartMonitoringAsync().ConfigureAwait(false); }
                    catch (Exception ex) { _logger?.LogWarning(ex, "Failed to start new provider after provider change"); }
                }
            }
            finally
            {
                _providerLock.Release();
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

        try
        {
            PendingProviderChangeTask?.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Pending provider change failed during disposal");
        }

        _providerLock.Wait();
        try
        {
            var provider = _currentProvider;
            if (provider != null)
            {
                provider.StatsUpdated -= OnProviderStatsUpdated;
                provider.ErrorOccurred -= OnProviderErrorOccurred;
                _currentProvider = null;
                provider.Dispose();
            }
        }
        finally
        {
            _providerLock.Release();
            _providerLock.Dispose();
        }
    }
}
