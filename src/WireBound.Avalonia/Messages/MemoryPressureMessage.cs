using WireBound.Core.Models;

namespace WireBound.Avalonia.Messages;

/// <summary>
/// Published via WeakReferenceMessenger when memory pressure state changes.
/// Subscribers: tray icon (color/tooltip), health strip (pulse), event persistence.
/// </summary>
/// <param name="Level">Current pressure level</param>
/// <param name="UsagePercent">Physical memory usage percentage (0-100)</param>
/// <param name="AvailableBytes">Available physical memory in bytes</param>
/// <param name="SwapUsedBytes">Active swap/page file usage in bytes</param>
/// <param name="Explanation">Human-readable reason, e.g. "RAM 91% for 45s, swap active (340 MB)"</param>
/// <param name="TopProcesses">Top memory consumers grouped by name, null if not yet available</param>
public record MemoryPressureMessage(
    MemoryPressureLevel Level,
    double UsagePercent,
    long AvailableBytes,
    long SwapUsedBytes,
    string Explanation,
    IReadOnlyList<ProcessMemoryInfo>? TopProcesses);

/// <summary>
/// Memory usage for a single process (grouped by name, summed across PIDs)
/// </summary>
/// <param name="ProcessName">Process name (e.g. "chrome")</param>
/// <param name="MemoryBytes">Total working set across all instances</param>
/// <param name="ProcessCount">Number of OS processes with this name</param>
public record ProcessMemoryInfo(
    string ProcessName,
    long MemoryBytes,
    int ProcessCount);
