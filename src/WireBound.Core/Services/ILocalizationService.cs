using System.Globalization;

namespace WireBound.Core.Services;

/// <summary>
/// Service for retrieving localized strings from resource files.
/// </summary>
public interface ILocalizationService
{
    /// <summary>
    /// Gets a localized string by its resource key.
    /// </summary>
    /// <param name="key">The resource key.</param>
    /// <returns>The localized string, or the key if not found.</returns>
    string GetString(string key);

    /// <summary>
    /// Gets a formatted localized string by its resource key.
    /// </summary>
    /// <param name="key">The resource key.</param>
    /// <param name="args">Format arguments.</param>
    /// <returns>The formatted localized string.</returns>
    string GetString(string key, params object[] args);

    /// <summary>
    /// Gets the current culture being used for localization.
    /// </summary>
    CultureInfo CurrentCulture { get; }
}
