using System.ComponentModel.DataAnnotations;

namespace WireBound.Core.Models;

/// <summary>
/// User-defined override for mapping an executable to an application category.
/// Built-in defaults are hardcoded; this entity stores user customizations.
/// </summary>
public class AppCategoryMapping
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Executable name without extension, case-insensitive (e.g., "chrome", "code")
    /// </summary>
    public string ExecutableName { get; set; } = string.Empty;

    /// <summary>
    /// Category name to assign (e.g., "Web Browsers", "Development Tools")
    /// </summary>
    public string CategoryName { get; set; } = string.Empty;

    /// <summary>
    /// Whether this mapping was created by the user (true) or is a built-in default (false).
    /// Only user-defined mappings are persisted in the database.
    /// </summary>
    public bool IsUserDefined { get; set; } = true;
}
