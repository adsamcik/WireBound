namespace WireBound.Core.Models;

/// <summary>
/// Memory pressure severity levels for the alert state machine
/// </summary>
public enum MemoryPressureLevel
{
    /// <summary>
    /// Memory usage is within normal parameters
    /// </summary>
    Normal = 0,

    /// <summary>
    /// Memory usage exceeds warning threshold and sustained duration
    /// </summary>
    Warning = 1,

    /// <summary>
    /// Memory usage exceeds critical threshold and sustained duration
    /// </summary>
    Critical = 2,
}
