using System.Runtime.Versioning;
using WireBound.IPC.Messages;
using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Linux.Services;

/// <summary>
/// Elevated process network provider that retrieves per-connection byte data
/// from the helper process via Unix domain socket IPC.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxElevatedProcessNetworkProvider : IProcessNetworkProvider
{
    private readonly IHelperConnection _connection;
    private volatile bool _monitoring;

    public LinuxElevatedProcessNetworkProvider(IHelperConnection connection)
    {
        _connection = connection;
    }

    public ProcessNetworkCapabilities Capabilities =>
        ProcessNetworkCapabilities.ConnectionList |
        ProcessNetworkCapabilities.ByteCounters |
        ProcessNetworkCapabilities.RealTimeBandwidth;

    public bool IsMonitoring => _monitoring;

#pragma warning disable CS0067 // Event will be used when real-time polling is implemented
    public event EventHandler<ProcessNetworkProviderEventArgs>? StatsUpdated;
#pragma warning restore CS0067
    public event EventHandler<ProcessNetworkProviderErrorEventArgs>? ErrorOccurred;

    public Task<bool> StartMonitoringAsync(CancellationToken cancellationToken = default)
    {
        _monitoring = true;
        return Task.FromResult(true);
    }

    public Task StopMonitoringAsync()
    {
        _monitoring = false;
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<ProcessNetworkStats>> GetProcessStatsAsync(CancellationToken cancellationToken = default)
    {
        if (!_connection.IsConnected)
            return [];

        try
        {
            var request = new ProcessStatsRequest();
            var response = await _connection.SendRequestAsync<ProcessStatsRequest, ProcessStatsResponse>(request, cancellationToken);

            if (!response.Success)
                return [];

            return response.Processes
                .Select(p => new ProcessNetworkStats
                {
                    ProcessId = p.ProcessId,
                    ProcessName = p.ProcessName,
                    SessionBytesSent = p.TotalBytesSent,
                    SessionBytesReceived = p.TotalBytesReceived
                })
                .ToList();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ProcessNetworkProviderErrorEventArgs(ex.Message));
            return [];
        }
    }

    public async Task<IReadOnlyList<ConnectionInfo>> GetActiveConnectionsAsync(CancellationToken cancellationToken = default)
    {
        if (!_connection.IsConnected)
            return [];

        try
        {
            var request = new ConnectionStatsRequest();
            var response = await _connection.SendRequestAsync<ConnectionStatsRequest, ConnectionStatsResponse>(request, cancellationToken);

            if (!response.Success)
                return [];

            return response.Processes
                .SelectMany(p => p.Connections.Select(c => new ConnectionInfo
                {
                    ProcessId = p.ProcessId,
                    LocalAddress = c.LocalAddress,
                    LocalPort = c.LocalPort,
                    RemoteAddress = c.RemoteAddress,
                    RemotePort = c.RemotePort,
                    Protocol = c.Protocol == 6 ? "TCP" : "UDP",
                    State = ConnectionState.Established
                }))
                .ToList();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ProcessNetworkProviderErrorEventArgs(ex.Message));
            return [];
        }
    }

    public async Task<IReadOnlyList<ConnectionStats>> GetConnectionStatsAsync(CancellationToken cancellationToken = default)
    {
        if (!_connection.IsConnected)
            return [];

        try
        {
            var request = new ConnectionStatsRequest();
            var response = await _connection.SendRequestAsync<ConnectionStatsRequest, ConnectionStatsResponse>(request, cancellationToken);

            if (!response.Success)
                return [];

            return response.Processes
                .SelectMany(p => p.Connections.Select(c => new ConnectionStats
                {
                    ProcessId = p.ProcessId,
                    ProcessName = p.ProcessName,
                    LocalAddress = c.LocalAddress,
                    LocalPort = c.LocalPort,
                    RemoteAddress = c.RemoteAddress,
                    RemotePort = c.RemotePort,
                    Protocol = c.Protocol == 6 ? "TCP" : "UDP",
                    BytesSent = c.BytesSent,
                    BytesReceived = c.BytesReceived,
                    HasByteCounters = true
                }))
                .ToList();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ProcessNetworkProviderErrorEventArgs(ex.Message));
            return [];
        }
    }

    public void Dispose()
    {
        _monitoring = false;
    }
}
