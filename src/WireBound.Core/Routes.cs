namespace WireBound.Core;

/// <summary>
/// Constants for navigation route names used throughout the application.
/// </summary>
public static class Routes
{
    public const string Dashboard = "Dashboard";
    public const string Charts = "Charts";
    public const string History = "History";
    public const string Applications = "Applications";
    public const string Connections = "Connections";
    public const string System = "System";
    public const string Settings = "Settings";

    /// <summary>
    /// Returns all available routes.
    /// </summary>
    public static IReadOnlyList<string> All { get; } = new[]
    {
        Dashboard, Charts, History, Applications, Connections, System, Settings
    };
}
