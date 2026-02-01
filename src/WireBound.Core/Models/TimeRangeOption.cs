namespace WireBound.Core.Models;

/// <summary>
/// Represents a time range option for chart displays.
/// Shared across Dashboard, Charts, and other views that use time-based filtering.
/// </summary>
public sealed class TimeRangeOption
{
    /// <summary>
    /// Short label for display in compact UI elements (e.g., "30s", "1m", "5m")
    /// </summary>
    public required string Label { get; init; }

    /// <summary>
    /// Duration in seconds that this time range represents
    /// </summary>
    public required int Seconds { get; init; }

    /// <summary>
    /// Human-readable description (e.g., "Last 30 seconds", "Last 1 minute")
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Standard time range options used across the application.
    /// </summary>
    public static IReadOnlyList<TimeRangeOption> StandardOptions { get; } =
    [
        new() { Label = "30s", Seconds = 30, Description = "Last 30 seconds" },
        new() { Label = "1m", Seconds = 60, Description = "Last 1 minute" },
        new() { Label = "5m", Seconds = 300, Description = "Last 5 minutes" },
        new() { Label = "15m", Seconds = 900, Description = "Last 15 minutes" },
        new() { Label = "1h", Seconds = 3600, Description = "Last 1 hour" }
    ];

    /// <summary>
    /// Extended time range options for historical views.
    /// </summary>
    public static IReadOnlyList<TimeRangeOption> ExtendedOptions { get; } =
    [
        new() { Label = "1h", Seconds = 3600, Description = "Last 1 hour" },
        new() { Label = "6h", Seconds = 21600, Description = "Last 6 hours" },
        new() { Label = "24h", Seconds = 86400, Description = "Last 24 hours" },
        new() { Label = "7d", Seconds = 604800, Description = "Last 7 days" }
    ];
}
