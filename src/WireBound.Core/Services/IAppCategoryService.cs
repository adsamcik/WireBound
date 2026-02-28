namespace WireBound.Core.Services;

/// <summary>
/// Maps executable names to application categories.
/// Has built-in defaults with user-overridable custom mappings from the database.
/// </summary>
public interface IAppCategoryService
{
    /// <summary>
    /// Get the category name for a given executable name.
    /// Matches case-insensitively, without file extension.
    /// Returns "Other" for unrecognized executables.
    /// </summary>
    /// <param name="executableName">Process name or executable filename (with or without extension)</param>
    /// <returns>Category name (e.g., "Web Browsers", "Development Tools")</returns>
    string GetCategory(string executableName);

    /// <summary>
    /// Get the category using a layered detection pipeline with richer context.
    /// Pipeline: user override → exe match → publisher mapping → OS metadata → path heuristics → parent process → "Other".
    /// Results are cached by executable path for performance.
    /// </summary>
    /// <param name="executableName">Process name or executable filename</param>
    /// <param name="executablePath">Full path to the executable (enables publisher/path detection)</param>
    /// <param name="processId">Process ID (enables parent process attribution)</param>
    /// <returns>Category name</returns>
    string GetCategory(string executableName, string? executablePath, int processId = 0);

    /// <summary>
    /// Get all known category names.
    /// </summary>
    IReadOnlyList<string> GetAllCategories();

    /// <summary>
    /// Load user-defined category overrides from the database.
    /// Call during initialization or after settings change.
    /// </summary>
    Task LoadUserOverridesAsync();
}
