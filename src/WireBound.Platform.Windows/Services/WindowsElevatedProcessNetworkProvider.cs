using System.Runtime.Versioning;
using WireBound.IPC.Messages;
using WireBound.Platform.Abstract.Helpers;
using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Windows.Services;

/// <summary>
/// Elevated process network provider that retrieves per-process byte counters
/// from the elevated helper over named-pipe IPC and enriches them into the
/// <see cref="ProcessNetworkStats"/> shape expected by the rest of the app.
/// </summary>
/// <remarks>
/// <para>
/// The helper returns raw cumulative byte totals per PID. This provider:
/// </para>
/// <list type="bullet">
///   <item>Derives a stable <c>AppIdentifier</c> from <c>ExecutablePath</c>
///         via <see cref="AppIdentity.ComputeAppIdentifier"/>. Without this,
///         <c>DataPersistenceService.SaveAppStatsAsync</c> silently filters
///         every record (the Applications tab would stay empty forever).</item>
///   <item>Resolves a friendly <c>DisplayName</c> via
///         <see cref="AppIdentity.ResolveDisplayName"/>.</item>
///   <item>Computes instantaneous download/upload speeds by diffing the
///         cumulative byte counts between successive helper polls. Without
///         this, peak speeds in the database would always be zero.</item>
///   <item>Tracks <c>FirstSeen</c> per PID so the UI can show how long a
///         process has been active in the current session.</item>
/// </list>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsElevatedProcessNetworkProvider : IProcessNetworkProvider, IAsyncDisposable
{
    private readonly IHelperConnection _connection;
    private volatile bool _monitoring;
    private CancellationTokenSource? _monitoringCts;
    private Task? _monitoringTask;

    private readonly Dictionary<int, PreviousSample> _previousSamples = [];
    private readonly Dictionary<int, DateTime> _firstSeen = [];
    private readonly object _stateLock = new();

    public WindowsElevatedProcessNetworkProvider(IHelperConnection connection)
    {
        _connection = connection;
    }

    public ProcessNetworkCapabilities Capabilities =>
        ProcessNetworkCapabilities.ConnectionList |
        ProcessNetworkCapabilities.ByteCounters |
        ProcessNetworkCapabilities.RealTimeBandwidth;

    public bool IsMonitoring => _monitoring;

    public event EventHandler<ProcessNetworkProviderEventArgs>? StatsUpdated;
    public event EventHandler<ProcessNetworkProviderErrorEventArgs>? ErrorOccurred;

    private void SurfaceHelperFailure(string responseName, string operationName, string? errorMessage)
    {
        var message = string.IsNullOrWhiteSpace(errorMessage)
            ? $"Helper returned unsuccessful {responseName} with no error message"
            : $"Helper {operationName} error: {errorMessage}";

        ErrorOccurred?.Invoke(this, new ProcessNetworkProviderErrorEventArgs(message));
    }

    public Task<bool> StartMonitoringAsync(CancellationToken cancellationToken = default)
    {
        if (_monitoring) return Task.FromResult(true);

        _monitoring = true;
        _monitoringCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _monitoringTask = PollAsync(_monitoringCts.Token);
        return Task.FromResult(true);
    }

    public async Task StopMonitoringAsync()
    {
        _monitoring = false;

        if (_monitoringCts != null)
        {
            await _monitoringCts.CancelAsync();

            if (_monitoringTask != null)
            {
                try { await _monitoringTask; }
                catch (OperationCanceledException) { }
            }

            _monitoringCts.Dispose();
            _monitoringCts = null;
            _monitoringTask = null;
        }
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(2000, cancellationToken);

                if (!_connection.IsConnected) continue;

                var stats = await GetProcessStatsAsync(cancellationToken);
                if (stats.Count > 0)
                {
                    StatsUpdated?.Invoke(this, new ProcessNetworkProviderEventArgs(
                        stats, DateTimeOffset.Now, TimeSpan.FromSeconds(2)));
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                ErrorOccurred?.Invoke(this, new ProcessNetworkProviderErrorEventArgs(ex.Message));
            }
        }
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
            {
                SurfaceHelperFailure(nameof(ProcessStatsResponse), "ProcessStats", response.ErrorMessage);
                return [];
            }

            return EnrichAndComputeSpeeds(response.Processes, DateTime.Now);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ProcessNetworkProviderErrorEventArgs(ex.Message));
            return [];
        }
    }

    /// <summary>
    /// Maps helper-side <see cref="ProcessByteStats"/> into fully populated
    /// <see cref="ProcessNetworkStats"/>, computing instantaneous speeds by
    /// diffing cumulative byte totals against the previous poll.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PID reuse and process restarts cause the cumulative counters to drop.
    /// When that happens we treat the current sample as a new baseline and
    /// emit zero speed for the interval rather than emitting a huge negative
    /// or wrap-around value.
    /// </para>
    /// </remarks>
    internal IReadOnlyList<ProcessNetworkStats> EnrichAndComputeSpeeds(
        IEnumerable<ProcessByteStats> raw,
        DateTime now)
    {
        var enriched = new List<ProcessNetworkStats>();
        var seenPids = new HashSet<int>();

        lock (_stateLock)
        {
            foreach (var p in raw)
            {
                seenPids.Add(p.ProcessId);

                long downloadBps = 0;
                long uploadBps = 0;
                if (_previousSamples.TryGetValue(p.ProcessId, out var prev))
                {
                    var elapsed = (now - prev.SampledAt).TotalSeconds;
                    if (elapsed > 0)
                    {
                        var rxDelta = p.TotalBytesReceived - prev.BytesReceived;
                        var txDelta = p.TotalBytesSent - prev.BytesSent;

                        // Negative delta = PID reuse or process restart — emit
                        // zero for this tick and let the baseline reset below.
                        if (rxDelta >= 0)
                            downloadBps = (long)(rxDelta / elapsed);
                        if (txDelta >= 0)
                            uploadBps = (long)(txDelta / elapsed);
                    }
                }

                _previousSamples[p.ProcessId] = new PreviousSample(
                    p.TotalBytesReceived, p.TotalBytesSent, now);

                if (!_firstSeen.TryGetValue(p.ProcessId, out var firstSeen))
                {
                    firstSeen = now;
                    _firstSeen[p.ProcessId] = firstSeen;
                }

                enriched.Add(new ProcessNetworkStats
                {
                    ProcessId = p.ProcessId,
                    ProcessName = p.ProcessName,
                    ExecutablePath = p.ExecutablePath,
                    AppIdentifier = AppIdentity.ComputeAppIdentifier(p.ExecutablePath),
                    DisplayName = AppIdentity.ResolveDisplayName(p.ExecutablePath, p.ProcessName),
                    SessionBytesReceived = p.TotalBytesReceived,
                    SessionBytesSent = p.TotalBytesSent,
                    LoopbackBytesReceived = p.LoopbackBytesReceived,
                    LoopbackBytesSent = p.LoopbackBytesSent,
                    DownloadSpeedBps = downloadBps,
                    UploadSpeedBps = uploadBps,
                    FirstSeen = firstSeen,
                    LastSeen = now
                });
            }

            // Evict tracking state for PIDs that disappeared so the dictionaries
            // don't grow unbounded over a long session.
            var stalePids = _previousSamples.Keys.Where(pid => !seenPids.Contains(pid)).ToList();
            foreach (var pid in stalePids)
            {
                _previousSamples.Remove(pid);
                _firstSeen.Remove(pid);
            }
        }

        return enriched;
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
            {
                SurfaceHelperFailure(nameof(ConnectionStatsResponse), "ConnectionStats", response.ErrorMessage);
                return [];
            }

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
            {
                SurfaceHelperFailure(nameof(ConnectionStatsResponse), "ConnectionStats", response.ErrorMessage);
                return [];
            }

            // The helper's per-connection byte counters are zero on Win10/11
            // because ETW indexes its data-transfer events by Tcb pointer
            // while the helper's connection enumeration is keyed by
            // local:port-remote:port endpoint quads — the two keyspaces
            // don't overlap (lifecycle events expose Tcb + addresses but
            // we don't currently parse the SocketAddress binary blob into
            // a quad-key lookup table).
            //
            // ProcessConnectionStats.BytesSent / BytesReceived at the
            // PROCESS level ARE accurate because they aggregate over the
            // Tcb-keyed ETW dictionary. To make the summary cards on the
            // Connections tab correct, we attach the per-process total to
            // the FIRST connection of each process and zero on subsequent
            // entries. Summing across the flattened list then yields the
            // correct total. Per-connection breakdowns are still
            // imprecise but the cards (Received / Sent) finally show
            // real numbers.
            return response.Processes
                .SelectMany(p => p.Connections.Select((c, index) => new ConnectionStats
                {
                    ProcessId = p.ProcessId,
                    ProcessName = p.ProcessName,
                    LocalAddress = c.LocalAddress,
                    LocalPort = c.LocalPort,
                    RemoteAddress = c.RemoteAddress,
                    RemotePort = c.RemotePort,
                    Protocol = c.Protocol == 6 ? "TCP" : "UDP",
                    BytesSent = index == 0 ? p.BytesSent : 0,
                    BytesReceived = index == 0 ? p.BytesReceived : 0,
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

    public async ValueTask DisposeAsync()
    {
        _monitoring = false;
        if (_monitoringCts is not null)
        {
            await _monitoringCts.CancelAsync().ConfigureAwait(false);
            if (_monitoringTask is not null)
            {
                try { await _monitoringTask.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }

            _monitoringCts.Dispose();
            _monitoringCts = null;
            _monitoringTask = null;
        }
    }

    public void Dispose()
    {
        // Prefer DisposeAsync so the poll loop can finish without blocking a sync context.
        // This fallback may deadlock if a UI thread synchronously blocks while continuations need it.
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private readonly record struct PreviousSample(long BytesReceived, long BytesSent, DateTime SampledAt);
}
