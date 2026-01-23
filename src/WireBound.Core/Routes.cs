namespace WireBound.Core;

/// <summary>
/// Constants for navigation route names used throughout the application.
/// </summary>
public static class Routes
{
    public const string Overview = "Overview";
    public const string Charts = "Charts";
    public const string Insights = "Insights";
    public const string Applications = "Applications";
    public const string Connections = "Connections";
    public const string System = "System";
    public const string Settings = "Settings";

    /// <summary>
    /// Returns all available routes.
    /// </summary>
    public static IReadOnlyList<string> All { get; } = new[]
    {
        Overview, Charts, Insights, Applications, Connections, System, Settings
    };
}
