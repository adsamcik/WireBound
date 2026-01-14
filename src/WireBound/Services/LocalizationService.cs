using System.Globalization;
using System.Resources;
using WireBound.Core.Services;

namespace WireBound.Services;

/// <summary>
/// Implementation of localization service using .resx resource files.
/// </summary>
public sealed class LocalizationService : ILocalizationService
{
    private readonly ResourceManager _resourceManager;

    public CultureInfo CurrentCulture => CultureInfo.CurrentUICulture;

    public LocalizationService()
    {
        _resourceManager = new ResourceManager(
            "WireBound.Resources.Strings.AppStrings", 
            typeof(LocalizationService).Assembly);
    }

    public string GetString(string key)
    {
        try
        {
            return _resourceManager.GetString(key, CurrentCulture) ?? key;
        }
        catch (MissingManifestResourceException)
        {
            return key;
        }
    }

    public string GetString(string key, params object[] args)
    {
        var format = GetString(key);
        try
        {
            return string.Format(CurrentCulture, format, args);
        }
        catch (FormatException)
        {
            return format;
        }
    }
}

/// <summary>
/// Static helper for accessing localized strings without DI.
/// Useful for XAML markup extensions and static contexts.
/// </summary>
public static class Strings
{
    private static ILocalizationService? _service;

    internal static void Initialize(ILocalizationService service)
    {
        _service = service;
    }

    /// <summary>
    /// Gets a localized string by key.
    /// </summary>
    public static string Get(string key) => _service?.GetString(key) ?? key;

    /// <summary>
    /// Gets a formatted localized string.
    /// </summary>
    public static string Get(string key, params object[] args) => _service?.GetString(key, args) ?? key;

    // Commonly used strings as properties for compile-time safety
    public static string Settings_Title => Get(nameof(Settings_Title));
    public static string Settings_Subtitle => Get(nameof(Settings_Subtitle));
    public static string Settings_StartWithWindows => Get(nameof(Settings_StartWithWindows));
    public static string Settings_StartWithWindowsDescription => Get(nameof(Settings_StartWithWindowsDescription));
    public static string Settings_StartupDisabledByUser => Get(nameof(Settings_StartupDisabledByUser));
    public static string Settings_StartupDisabledByPolicy => Get(nameof(Settings_StartupDisabledByPolicy));
    public static string Settings_StartupEnableFailed => Get(nameof(Settings_StartupEnableFailed));
    public static string Settings_SavedSuccessfully => Get(nameof(Settings_SavedSuccessfully));
}
