using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Stub.Services;

/// <summary>
/// Stub implementation of IProcessNetworkProvider for unsupported platforms.
/// Returns empty data and does no actual monitoring.
/// </summary>
public sealed class StubProcessNetworkProvider : IProcessNetworkProvider
{
    public ProcessNetworkCapabilities Capabilities => ProcessNetworkCapabilities.None;
    public bool IsMonitoring => false;

    public event EventHandler<ProcessNetworkProviderEventArgs>? StatsUpdated;
    public event EventHandler<ProcessNetworkProviderErrorEventArgs>? ErrorOccurred;

    public Task<bool> StartMonitoringAsync(CancellationToken cancellationToken = default)
    {
        // Stub - no actual monitoring
        return Task.FromResult(false);
    }

    public Task StopMonitoringAsync()
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ProcessNetworkStats>> GetProcessStatsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ProcessNetworkStats>>([]);
    }

    public Task<IReadOnlyList<ConnectionInfo>> GetActiveConnectionsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ConnectionInfo>>([]);
    }

    public Task<IReadOnlyList<ConnectionStats>> GetConnectionStatsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ConnectionStats>>([]);
    }

    public void Dispose()
    {
        // Nothing to dispose
    }
}
