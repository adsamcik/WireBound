namespace WireBound.Core.Models;

/// <summary>
/// A point-in-time, per-PID resource snapshot for the live process monitor.
/// </summary>
public sealed class ProcessUsageSnapshot
{
    /// <summary>
    /// Operating-system process identifier.
    /// </summary>
    public int ProcessId { get; init; }

    /// <summary>
    /// Process name reported by the operating system.
    /// </summary>
    public string ProcessName { get; init; } = string.Empty;

    /// <summary>
    /// Full executable path when the operating system permits it.
    /// </summary>
    public string ExecutablePath { get; init; } = string.Empty;

    /// <summary>
    /// Private committed memory in bytes.
    /// </summary>
    public long PrivateBytes { get; init; }

    /// <summary>
    /// Physical working-set memory in bytes.
    /// </summary>
    public long WorkingSetBytes { get; init; }

    /// <summary>
    /// Total processor time in 100-nanosecond ticks from the raw platform sample.
    /// </summary>
    public long CpuTimeTicks { get; init; }

    /// <summary>
    /// CPU usage calculated from this process's prior sample. The first sample
    /// after a reset has no baseline and reports zero.
    /// </summary>
    public double CpuPercent { get; init; }

    /// <summary>
    /// Whether <see cref="CpuPercent"/> was calculated from a valid prior
    /// sample. The first sample after a reset is false.
    /// </summary>
    public bool HasCpuSample { get; init; }

    /// <summary>
    /// Current download rate in bytes per second.
    /// </summary>
    public long DownloadSpeedBps { get; init; }

    /// <summary>
    /// Current upload rate in bytes per second.
    /// </summary>
    public long UploadSpeedBps { get; init; }

    /// <summary>
    /// Session bytes received by this process.
    /// </summary>
    public long SessionBytesReceived { get; init; }

    /// <summary>
    /// Session bytes sent by this process.
    /// </summary>
    public long SessionBytesSent { get; init; }

    /// <summary>
    /// Whether the network fields were joined from an already-running
    /// per-process network monitor.
    /// </summary>
    public bool HasNetworkStats { get; init; }
}
