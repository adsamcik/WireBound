using WireBound.Core.Models;

namespace WireBound.Core.Services;

/// <summary>
/// Captures live resource usage for individual operating-system processes.
/// </summary>
/// <remarks>
/// This service is intentionally pull-based: it owns no polling loop and does
/// not start process-network monitoring. A visible page can call
/// <see cref="CaptureAsync"/> at its chosen cadence and stop doing so as soon
/// as it is no longer active.
/// </remarks>
public interface IProcessUsageService
{
    /// <summary>
    /// Captures one per-PID resource snapshot and, when available, joins the
    /// current snapshot from already-running process-network monitoring.
    /// CPU usage is calculated from the prior successful capture for each PID.
    /// Concurrent captures are serialized by the implementation.
    /// </summary>
    Task<IReadOnlyList<ProcessUsageSnapshot>> CaptureAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears CPU baselines. The next successful capture reports zero CPU until
    /// a subsequent sample establishes a time delta.
    /// </summary>
    void Reset();
}
