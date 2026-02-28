namespace WireBound.Platform.Abstract.Services;

/// <summary>
/// Platform-specific provider for detecting known game executables.
/// Uses registry entries, launcher manifests, and platform databases
/// to identify game processes for category classification.
/// </summary>
public interface IGameDetectionProvider
{
    /// <summary>
    /// Check if the given executable path belongs to a known game.
    /// Compares against cached data from platform-specific sources
    /// (e.g., Windows GameConfigStore, launcher install directories).
    /// </summary>
    /// <param name="executablePath">Full path to the executable</param>
    /// <returns>True if the executable is recognized as a game</returns>
    bool IsKnownGame(string executablePath);

    /// <summary>
    /// Initialize or refresh the internal database of known game executables.
    /// Scans platform-specific sources: registry, launcher manifests, databases.
    /// Should be called once at startup and optionally refreshed periodically.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
