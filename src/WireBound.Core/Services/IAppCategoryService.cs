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
    /// Get all known category names.
    /// </summary>
    IReadOnlyList<string> GetAllCategories();

    /// <summary>
    /// Load user-defined category overrides from the database.
    /// Call during initialization or after settings change.
    /// </summary>
    Task LoadUserOverridesAsync();
}
