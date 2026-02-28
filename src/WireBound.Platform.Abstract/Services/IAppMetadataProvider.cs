namespace WireBound.Platform.Abstract.Services;

/// <summary>
/// Platform-specific provider for application metadata used in categorization.
/// Extracts publisher info, OS-level categories, and process relationships.
/// </summary>
public interface IAppMetadataProvider
{
    /// <summary>
    /// Get the publisher/company name for an executable.
    /// Windows: FileVersionInfo.CompanyName or Authenticode certificate subject.
    /// Linux: may return null (publisher info not typically available).
    /// </summary>
    /// <param name="executablePath">Full path to the executable</param>
    /// <returns>Publisher name, or null if unavailable</returns>
    string? GetPublisher(string executablePath);

    /// <summary>
    /// Get a category for an executable from OS-level metadata.
    /// Windows: returns null (no OS-level category taxonomy).
    /// Linux: parses .desktop files for freedesktop.org Categories.
    /// </summary>
    /// <param name="executableName">Executable name without extension</param>
    /// <returns>WireBound category name, or null if not found</returns>
    string? GetCategoryFromOsMetadata(string executableName);

    /// <summary>
    /// Get the executable name of a process's parent.
    /// Used for parent process attribution (e.g., child of Steam → Gaming).
    /// </summary>
    /// <param name="processId">PID of the process to look up</param>
    /// <returns>Parent process executable name (without extension), or null</returns>
    string? GetParentProcessName(int processId);

    /// <summary>
    /// Initialize or refresh cached metadata (e.g., .desktop file index).
    /// Called once at startup. Implementations should be tolerant of failures.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
