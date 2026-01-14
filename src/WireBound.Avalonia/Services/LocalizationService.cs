using System.Globalization;
using WireBound.Core.Services;

namespace WireBound.Avalonia.Services;

/// <summary>
/// Localization service for accessing translated strings
/// </summary>
public sealed class LocalizationService : ILocalizationService
{
    // Simple English strings for now - can be expanded with resource files
    private readonly Dictionary<string, string> _strings = new()
    {
        { "Settings_StartWithWindows", "Start with Windows" },
        { "Settings_StartWithWindowsDescription", "Launch WireBound when Windows starts" },
        { "Settings_StartupDisabledByUser", "Startup has been disabled in Task Manager" },
        { "Settings_StartupDisabledByPolicy", "Startup has been disabled by group policy" },
        { "Dashboard_Title", "Dashboard" },
        { "Charts_Title", "Live Chart" },
        { "History_Title", "History" },
        { "Settings_Title", "Settings" },
        { "Applications_Title", "Applications" }
    };

    public CultureInfo CurrentCulture => CultureInfo.CurrentUICulture;

    public string GetString(string key)
    {
        return _strings.TryGetValue(key, out var value) ? value : key;
    }

    public string GetString(string key, params object[] args)
    {
        var template = GetString(key);
        return string.Format(CurrentCulture, template, args);
    }
}

/// <summary>
/// Static accessor for localized strings (used by markup extension)
/// </summary>
public static class Strings
{
    private static ILocalizationService? _service;

    public static void Initialize(ILocalizationService service)
    {
        _service = service;
    }

    public static string Get(string key)
    {
        return _service?.GetString(key) ?? key;
    }
}
