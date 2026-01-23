namespace WireBound.Core.Models;

/// <summary>
/// Combined system statistics snapshot (CPU, Memory, and optionally GPU)
/// </summary>
public class SystemStats
{
    /// <summary>
    /// Timestamp when stats were captured
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;

    /// <summary>
    /// CPU statistics
    /// </summary>
    public CpuStats Cpu { get; set; } = new();

    /// <summary>
    /// Memory statistics
    /// </summary>
    public MemoryStats Memory { get; set; } = new();
}
