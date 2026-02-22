using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WireBound.Core.Data;
using WireBound.Core.Services;

namespace WireBound.Avalonia.Services;

/// <summary>
/// Maps executable names to application categories using built-in defaults
/// and user-defined overrides from the database.
/// </summary>
public sealed class AppCategoryService : IAppCategoryService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AppCategoryService>? _logger;

    // Merged dictionary: built-in defaults + user overrides (user wins)
    // Volatile ensures visibility when _mappings is replaced atomically from LoadUserOverridesAsync
    private volatile IReadOnlyDictionary<string, string> _mappings;
    private volatile bool _loaded;

    public const string CategoryWebBrowsers = "Web Browsers";
    public const string CategoryDevTools = "Development Tools";
    public const string CategoryCommunication = "Communication";
    public const string CategoryMedia = "Media";
    public const string CategoryGaming = "Gaming";
    public const string CategorySystem = "System Services";
    public const string CategoryOffice = "Office";
    public const string CategoryOther = "Other";

    private static readonly IReadOnlyList<string> AllCategoryNames =
    [
        CategoryWebBrowsers,
        CategoryDevTools,
        CategoryCommunication,
        CategoryMedia,
        CategoryGaming,
        CategorySystem,
        CategoryOffice,
        CategoryOther
    ];

    /// <summary>
    /// Built-in executable-to-category mappings. Keyed by lowercase exe name without extension.
    /// </summary>
    private static readonly Dictionary<string, string> DefaultMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        // Web Browsers
        ["chrome"] = CategoryWebBrowsers,
        ["firefox"] = CategoryWebBrowsers,
        ["msedge"] = CategoryWebBrowsers,
        ["opera"] = CategoryWebBrowsers,
        ["brave"] = CategoryWebBrowsers,
        ["vivaldi"] = CategoryWebBrowsers,
        ["arc"] = CategoryWebBrowsers,
        ["safari"] = CategoryWebBrowsers,
        ["waterfox"] = CategoryWebBrowsers,
        ["librewolf"] = CategoryWebBrowsers,
        ["chromium"] = CategoryWebBrowsers,
        ["tor"] = CategoryWebBrowsers,

        // Development Tools
        ["devenv"] = CategoryDevTools,
        ["code"] = CategoryDevTools,
        ["rider"] = CategoryDevTools,
        ["rider64"] = CategoryDevTools,
        ["idea64"] = CategoryDevTools,
        ["idea"] = CategoryDevTools,
        ["webstorm64"] = CategoryDevTools,
        ["pycharm64"] = CategoryDevTools,
        ["goland64"] = CategoryDevTools,
        ["clion64"] = CategoryDevTools,
        ["datagrip64"] = CategoryDevTools,
        ["dotnet"] = CategoryDevTools,
        ["node"] = CategoryDevTools,
        ["python"] = CategoryDevTools,
        ["python3"] = CategoryDevTools,
        ["java"] = CategoryDevTools,
        ["javaw"] = CategoryDevTools,
        ["cargo"] = CategoryDevTools,
        ["go"] = CategoryDevTools,
        ["rustc"] = CategoryDevTools,
        ["msbuild"] = CategoryDevTools,
        ["git"] = CategoryDevTools,
        ["docker"] = CategoryDevTools,
        ["docker-compose"] = CategoryDevTools,
        ["wt"] = CategoryDevTools, // Windows Terminal
        ["windowsterminal"] = CategoryDevTools,
        ["alacritty"] = CategoryDevTools,
        ["kitty"] = CategoryDevTools,
        ["sublime_text"] = CategoryDevTools,
        ["notepad++"] = CategoryDevTools,

        // Communication
        ["teams"] = CategoryCommunication,
        ["ms-teams"] = CategoryCommunication,
        ["slack"] = CategoryCommunication,
        ["discord"] = CategoryCommunication,
        ["zoom"] = CategoryCommunication,
        ["telegram"] = CategoryCommunication,
        ["signal"] = CategoryCommunication,
        ["skype"] = CategoryCommunication,
        ["whatsapp"] = CategoryCommunication,
        ["element"] = CategoryCommunication,

        // Media
        ["spotify"] = CategoryMedia,
        ["vlc"] = CategoryMedia,
        ["wmplayer"] = CategoryMedia,
        ["foobar2000"] = CategoryMedia,
        ["obs64"] = CategoryMedia,
        ["obs"] = CategoryMedia,
        ["audacity"] = CategoryMedia,
        ["gimp"] = CategoryMedia,
        ["photoshop"] = CategoryMedia,
        ["premiere"] = CategoryMedia,
        ["afterfx"] = CategoryMedia,
        ["blender"] = CategoryMedia,
        ["inkscape"] = CategoryMedia,
        ["mpv"] = CategoryMedia,
        ["mpc-hc64"] = CategoryMedia,
        ["foobar2000"] = CategoryMedia,

        // Gaming
        ["steam"] = CategoryGaming,
        ["steamwebhelper"] = CategoryGaming,
        ["epicgameslauncher"] = CategoryGaming,
        ["unrealcefsubprocess"] = CategoryGaming,
        ["origin"] = CategoryGaming,
        ["gog"] = CategoryGaming,
        ["gogclient"] = CategoryGaming,
        ["battle.net"] = CategoryGaming,
        ["ubisoft"] = CategoryGaming,
        ["upc"] = CategoryGaming,

        // System Services
        ["svchost"] = CategorySystem,
        ["csrss"] = CategorySystem,
        ["lsass"] = CategorySystem,
        ["services"] = CategorySystem,
        ["smss"] = CategorySystem,
        ["wininit"] = CategorySystem,
        ["winlogon"] = CategorySystem,
        ["dwm"] = CategorySystem,
        ["explorer"] = CategorySystem,
        ["runtimebroker"] = CategorySystem,
        ["searchhost"] = CategorySystem,
        ["searchindexer"] = CategorySystem,
        ["sihost"] = CategorySystem,
        ["fontdrvhost"] = CategorySystem,
        ["ctfmon"] = CategorySystem,
        ["conhost"] = CategorySystem,
        ["spoolsv"] = CategorySystem,
        ["taskhostw"] = CategorySystem,
        ["audiodg"] = CategorySystem,
        // Linux
        ["systemd"] = CategorySystem,
        ["journald"] = CategorySystem,
        ["dbus-daemon"] = CategorySystem,
        ["networkmanager"] = CategorySystem,
        ["gdm"] = CategorySystem,
        ["gnome-shell"] = CategorySystem,
        ["kwin"] = CategorySystem,
        ["plasmashell"] = CategorySystem,
        ["pulseaudio"] = CategorySystem,
        ["pipewire"] = CategorySystem,
        ["xorg"] = CategorySystem,
        ["xwayland"] = CategorySystem,

        // Office
        ["winword"] = CategoryOffice,
        ["excel"] = CategoryOffice,
        ["powerpnt"] = CategoryOffice,
        ["outlook"] = CategoryOffice,
        ["onenote"] = CategoryOffice,
        ["thunderbird"] = CategoryOffice,
        ["libreoffice"] = CategoryOffice,
        ["soffice"] = CategoryOffice,
        ["acrobat"] = CategoryOffice,
        ["acrord32"] = CategoryOffice,
        ["notion"] = CategoryOffice,
        ["obsidian"] = CategoryOffice,
    };

    public AppCategoryService(
        IServiceProvider serviceProvider,
        ILogger<AppCategoryService>? logger = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _mappings = new Dictionary<string, string>(DefaultMappings, StringComparer.OrdinalIgnoreCase);
    }

    public string GetCategory(string executableName)
    {
        if (string.IsNullOrWhiteSpace(executableName))
            return CategoryOther;

        // Strip extension if present
        var name = Path.GetFileNameWithoutExtension(executableName);
        if (string.IsNullOrWhiteSpace(name))
            name = executableName;

        return _mappings.TryGetValue(name, out var category) ? category : CategoryOther;
    }

    public IReadOnlyList<string> GetAllCategories() => AllCategoryNames;

    public async Task LoadUserOverridesAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();
            var overrides = await context.AppCategoryMappings
                .Where(m => m.IsUserDefined)
                .ToListAsync();

            // Start from defaults, then apply user overrides
            var merged = new Dictionary<string, string>(DefaultMappings, StringComparer.OrdinalIgnoreCase);
            foreach (var mapping in overrides)
            {
                merged[mapping.ExecutableName] = mapping.CategoryName;
            }

            _mappings = merged;
            _loaded = true;

            _logger?.LogDebug("Loaded {Count} user category overrides", overrides.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load user category overrides, using defaults");
        }
    }
}
