using WireBound.Core.Models;
using WireBound.Core.Services;
using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Avalonia.Services;

/// <summary>
/// Pull-based per-PID resource sampler used by the live process page.
/// </summary>
/// <remarks>
/// The sampler deliberately does not own a timer and never starts or stops
/// <see cref="IProcessNetworkService"/>. This keeps process enumeration active
/// only while a visible consumer requests snapshots and leaves persistent
/// network-monitoring ownership with Settings.
/// </remarks>
public sealed class ProcessUsageService : IProcessUsageService
{
    private readonly IProcessResourceProvider _resourceProvider;
    private readonly IProcessNetworkService? _processNetworkService;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _captureGate = new(1, 1);
    private readonly object _baselineLock = new();
    private Dictionary<int, PreviousProcessSample> _previousByProcessId = [];
    private long? _previousCaptureTimestamp;

    public ProcessUsageService(
        IProcessResourceProvider resourceProvider,
        IProcessNetworkService? processNetworkService = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(resourceProvider);

        _resourceProvider = resourceProvider;
        _processNetworkService = processNetworkService;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProcessUsageSnapshot>> CaptureAsync(CancellationToken cancellationToken = default)
    {
        await _captureGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Platform providers can expose a Task-based API while still doing
            // their process walk synchronously before returning Task.FromResult.
            // Always schedule that work away from the UI caller so opening or
            // manually refreshing the page cannot block input or rendering.
            var resources = await Task.Run(
                    () => _resourceProvider.GetProcessResourceDataAsync(cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            // CPU deltas need a monotonic interval. Wall-clock time can jump
            // because of NTP, daylight-saving changes, or manual edits.
            var timestamp = _timeProvider.GetTimestamp();
            var networkByProcessId = GetCurrentNetworkStats();
            cancellationToken.ThrowIfCancellationRequested();

            lock (_baselineLock)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var elapsedTicks = _previousCaptureTimestamp is { } previousCaptureTimestamp
                    ? Math.Max(0, _timeProvider.GetElapsedTime(previousCaptureTimestamp, timestamp).Ticks)
                    : 0L;
                var currentByProcessId = new Dictionary<int, PreviousProcessSample>(resources.Count);
                var entries = new List<ProcessUsageSnapshot>(resources.Count);

                foreach (var resource in resources)
                {
                    _previousByProcessId.TryGetValue(resource.ProcessId, out var previous);
                    var hasCpuSample = previous is not null
                                       && elapsedTicks > 0
                                       && previous.Matches(resource);
                    var cpuPercent = hasCpuSample
                        ? CalculateCpuPercent(resource, previous!, elapsedTicks)
                        : 0;
                    currentByProcessId[resource.ProcessId] = PreviousProcessSample.From(resource);

                    networkByProcessId.TryGetValue(resource.ProcessId, out var networkStats);
                    entries.Add(new ProcessUsageSnapshot
                    {
                        ProcessId = resource.ProcessId,
                        ProcessName = resource.ProcessName,
                        ExecutablePath = resource.ExecutablePath,
                        PrivateBytes = resource.PrivateBytes,
                        WorkingSetBytes = resource.WorkingSetBytes,
                        CpuTimeTicks = resource.CpuTimeTicks,
                        CpuPercent = cpuPercent,
                        HasCpuSample = hasCpuSample,
                        DownloadSpeedBps = networkStats?.DownloadSpeedBps ?? 0,
                        UploadSpeedBps = networkStats?.UploadSpeedBps ?? 0,
                        SessionBytesReceived = networkStats?.SessionBytesReceived ?? 0,
                        SessionBytesSent = networkStats?.SessionBytesSent ?? 0,
                        HasNetworkStats = networkStats is not null
                    });
                }

                // Commit the CPU baseline only after all cancellable work has
                // completed. A canceled capture must never contaminate the next
                // CPU calculation.
                cancellationToken.ThrowIfCancellationRequested();
                _previousByProcessId = currentByProcessId;
                _previousCaptureTimestamp = timestamp;

                return entries.AsReadOnly();
            }
        }
        finally
        {
            _captureGate.Release();
        }
    }

    /// <inheritdoc />
    public void Reset()
    {
        lock (_baselineLock)
        {
            _previousByProcessId = [];
            _previousCaptureTimestamp = null;
        }
    }

    private IReadOnlyDictionary<int, ProcessNetworkStats> GetCurrentNetworkStats()
    {
        // Do not cause a stopped provider to expose stale session data. The
        // process page consumes network metrics only when another owner (such
        // as the explicit Settings preference) has enabled tracking.
        if (_processNetworkService is not { IsRunning: true })
        {
            return EmptyNetworkStats;
        }

        var stats = _processNetworkService.GetCurrentStats();
        if (stats.Count == 0)
        {
            return EmptyNetworkStats;
        }

        var byProcessId = new Dictionary<int, ProcessNetworkStats>(stats.Count);
        foreach (var stat in stats)
        {
            if (stat.ProcessId > 0)
            {
                byProcessId[stat.ProcessId] = stat;
            }
        }

        return byProcessId;
    }

    private static double CalculateCpuPercent(
        ProcessResourceData resource,
        PreviousProcessSample previous,
        long elapsedTicks)
    {
        var cpuDelta = resource.CpuTimeTicks - previous.CpuTimeTicks;
        if (cpuDelta <= 0)
        {
            return 0;
        }

        var processorCount = Math.Max(1, Environment.ProcessorCount);
        var cpuPercent = (double)cpuDelta / elapsedTicks / processorCount * 100;
        return Math.Clamp(cpuPercent, 0, 100);
    }

    private sealed class PreviousProcessSample
    {
        public long CpuTimeTicks { get; init; }
        public string ProcessName { get; init; } = string.Empty;
        public string ExecutablePath { get; init; } = string.Empty;

        public static PreviousProcessSample From(ProcessResourceData resource)
        {
            return new PreviousProcessSample
            {
                CpuTimeTicks = resource.CpuTimeTicks,
                ProcessName = resource.ProcessName,
                ExecutablePath = resource.ExecutablePath
            };
        }

        public bool Matches(ProcessResourceData resource)
        {
            if (!string.Equals(ProcessName, resource.ProcessName, StringComparison.Ordinal))
            {
                return false;
            }

            // Metadata is sometimes unavailable for protected processes. Treat
            // an unavailable path as indeterminate rather than resetting a
            // valid CPU baseline on every sample.
            return string.IsNullOrEmpty(ExecutablePath)
                   || string.IsNullOrEmpty(resource.ExecutablePath)
                   || string.Equals(ExecutablePath, resource.ExecutablePath, StringComparison.Ordinal);
        }
    }

    private static readonly IReadOnlyDictionary<int, ProcessNetworkStats> EmptyNetworkStats =
        new Dictionary<int, ProcessNetworkStats>();
}
