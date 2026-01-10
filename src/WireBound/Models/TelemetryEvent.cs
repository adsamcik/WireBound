using System.ComponentModel.DataAnnotations;

namespace WireBound.Models;

/// <summary>
/// Represents a telemetry event for tracking app usage and network events
/// </summary>
public class TelemetryEvent
{
    [Key]
    public int Id { get; set; }
    
    /// <summary>
    /// When the event occurred
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;
    
    /// <summary>
    /// Category of event (Network, App, Performance, Error)
    /// </summary>
    public TelemetryCategory Category { get; set; }
    
    /// <summary>
    /// Event type (e.g., SessionStart, SessionEnd, SpeedSpike, ThresholdExceeded)
    /// </summary>
    public string EventType { get; set; } = string.Empty;
    
    /// <summary>
    /// Human-readable event description
    /// </summary>
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// Optional numeric value associated with the event
    /// </summary>
    public long? Value { get; set; }
    
    /// <summary>
    /// Optional secondary value (e.g., threshold for comparison)
    /// </summary>
    public long? SecondaryValue { get; set; }
    
    /// <summary>
    /// Network adapter associated with this event
    /// </summary>
    public string? AdapterId { get; set; }
    
    /// <summary>
    /// Additional metadata as JSON
    /// </summary>
    public string? Metadata { get; set; }
}

/// <summary>
/// Categories of telemetry events
/// </summary>
public enum TelemetryCategory
{
    /// <summary>
    /// Network-related events (speed spikes, threshold exceeded)
    /// </summary>
    Network,
    
    /// <summary>
    /// Application events (startup, shutdown, settings change)
    /// </summary>
    App,
    
    /// <summary>
    /// Performance events (slow polls, database operations)
    /// </summary>
    Performance,
    
    /// <summary>
    /// Error events
    /// </summary>
    Error
}
