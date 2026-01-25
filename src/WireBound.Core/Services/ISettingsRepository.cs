using WireBound.Core.Models;

namespace WireBound.Core.Services;

/// <summary>
/// Repository for application settings persistence.
/// </summary>
public interface ISettingsRepository
{
    /// <summary>
    /// Get app settings.
    /// </summary>
    Task<AppSettings> GetSettingsAsync();

    /// <summary>
    /// Save app settings.
    /// </summary>
    Task SaveSettingsAsync(AppSettings settings);
}
