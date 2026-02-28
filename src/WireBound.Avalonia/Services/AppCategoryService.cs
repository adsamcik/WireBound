using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WireBound.Core.Data;
using WireBound.Core.Services;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Avalonia.Services;

/// <summary>
/// Maps executable names to application categories using a layered detection pipeline:
/// 1. User override (DB)  2. Exe name match  3. Publisher mapping
/// 4. Game detection (GameConfigStore + launchers)  5. OS metadata (.desktop/AppStream)
/// 6. Path heuristics  7. Parent process  8. "Other"
/// </summary>
public sealed class AppCategoryService : IAppCategoryService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IAppMetadataProvider _metadataProvider;
    private readonly IGameDetectionProvider _gameDetectionProvider;
    private readonly ILogger<AppCategoryService>? _logger;

    // Merged dictionary: built-in defaults + user overrides (user wins)
    private volatile IReadOnlyDictionary<string, string> _mappings;

    // Cache for pipeline results keyed by exe path (avoids repeated expensive lookups)
    private readonly ConcurrentDictionary<string, string> _pipelineCache = new(StringComparer.OrdinalIgnoreCase);

    // Transparent parents: processes that are launchers/hosts, skip when walking parent tree
    private static readonly HashSet<string> TransparentParents = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "cmd", "powershell", "pwsh", "bash", "sh", "zsh", "fish",
        "wt", "windowsterminal", "conhost", "gnome-terminal-server", "konsole",
        "alacritty", "kitty", "xterm", "systemd", "init", "launchd",
        "sudo", "su", "dbus-launch", "dbus-daemon", "svchost", "services",
        "runtimebroker", "sihost", "taskhostw", "wininit", "csrss"
    };

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

    /// <summary>
    /// Publisher/company name substring → category mappings.
    /// Uses Contains matching so "JetBrains s.r.o." matches the "JetBrains" rule.
    /// Ordered by specificity — more specific matches are checked first.
    /// </summary>
    internal static readonly (string PublisherSubstring, string Category)[] PublisherMappings =
    [
        // Development Tools
        ("JetBrains", CategoryDevTools),
        ("GitHub", CategoryDevTools),
        ("Docker", CategoryDevTools),
        ("Git", CategoryDevTools),
        ("Node.js", CategoryDevTools),
        ("Python Software Foundation", CategoryDevTools),
        ("Sublime HQ", CategoryDevTools),
        ("Notepad++", CategoryDevTools),
        ("Eclipse Foundation", CategoryDevTools),
        ("Apache Software Foundation", CategoryDevTools),
        ("HashiCorp", CategoryDevTools),
        ("Postman", CategoryDevTools),

        // Communication
        ("Slack Technologies", CategoryCommunication),
        ("Discord", CategoryCommunication),
        ("Zoom Video Communications", CategoryCommunication),
        ("Telegram", CategoryCommunication),
        ("Signal Messenger", CategoryCommunication),
        ("Signal Foundation", CategoryCommunication),

        // Media
        ("Spotify", CategoryMedia),
        ("VideoLAN", CategoryMedia),
        ("OBS Project", CategoryMedia),
        ("Audacity", CategoryMedia),
        ("GIMP", CategoryMedia),
        ("Inkscape", CategoryMedia),
        ("Blender Foundation", CategoryMedia),
        ("Adobe", CategoryMedia),
        ("HandBrake", CategoryMedia),
        ("Plex", CategoryMedia),

        // Gaming
        ("Valve", CategoryGaming),
        ("Epic Games", CategoryGaming),
        ("Electronic Arts", CategoryGaming),
        ("Ubisoft", CategoryGaming),
        ("Riot Games", CategoryGaming),
        ("Blizzard Entertainment", CategoryGaming),
        ("CD PROJEKT", CategoryGaming),
        ("GOG.com", CategoryGaming),
        ("Unity Technologies", CategoryGaming),
        ("Steam", CategoryGaming),

        // Web Browsers
        ("Mozilla", CategoryWebBrowsers),
        ("Brave Software", CategoryWebBrowsers),
        ("Vivaldi Technologies", CategoryWebBrowsers),
        ("Opera", CategoryWebBrowsers),
        ("The Chromium Authors", CategoryWebBrowsers),

        // Office
        ("LibreOffice", CategoryOffice),
        ("The Document Foundation", CategoryOffice),
        ("Notion Labs", CategoryOffice),

        // System — broad matches last (these publishers make many types of software)
        ("Google LLC", CategoryWebBrowsers), // Default for Google; Chrome is most common
        ("Microsoft Corporation", CategorySystem), // Default for Microsoft; most are system services
        ("Apple Inc.", CategorySystem),
    ];

    /// <summary>
    /// Path segments that hint at a category. Checked as directory names in the exe path.
    /// </summary>
    private static readonly (string PathSegment, string Category)[] PathHeuristics =
    [
        // Gaming platforms
        ("steamapps", CategoryGaming),
        ("Steam", CategoryGaming),
        ("Epic Games", CategoryGaming),
        ("GOG Galaxy", CategoryGaming),
        ("Riot Games", CategoryGaming),
        ("Battle.net", CategoryGaming),

        // Development tools
        ("JetBrains", CategoryDevTools),
        ("Visual Studio", CategoryDevTools),
        ("Microsoft VS Code", CategoryDevTools),
        ("nodejs", CategoryDevTools),

        // System
        ("Windows\\System32", CategorySystem),
        ("Windows\\SysWOW64", CategorySystem),
        ("/usr/lib/systemd", CategorySystem),
        ("/usr/sbin", CategorySystem),
    ];

    public AppCategoryService(
        IServiceProvider serviceProvider,
        IAppMetadataProvider metadataProvider,
        IGameDetectionProvider gameDetectionProvider,
        ILogger<AppCategoryService>? logger = null)
    {
        _serviceProvider = serviceProvider;
        _metadataProvider = metadataProvider;
        _gameDetectionProvider = gameDetectionProvider;
        _logger = logger;
        _mappings = new Dictionary<string, string>(DefaultMappings, StringComparer.OrdinalIgnoreCase);
    }

    public string GetCategory(string executableName)
    {
        if (string.IsNullOrWhiteSpace(executableName))
            return CategoryOther;

        var name = NormalizeName(executableName);
        return _mappings.TryGetValue(name, out var category) ? category : CategoryOther;
    }

    public string GetCategory(string executableName, string? executablePath, int processId = 0)
    {
        if (string.IsNullOrWhiteSpace(executableName) && string.IsNullOrWhiteSpace(executablePath))
            return CategoryOther;

        var name = NormalizeName(
            !string.IsNullOrWhiteSpace(executablePath)
                ? Path.GetFileNameWithoutExtension(executablePath)
                : executableName);

        // Fast path: check pipeline cache first (keyed by exe path if available, else name)
        var cacheKey = !string.IsNullOrWhiteSpace(executablePath) ? executablePath : name;
        if (_pipelineCache.TryGetValue(cacheKey, out var cached))
            return cached;

        // Run the layered pipeline
        var result = RunPipeline(name, executablePath, processId);

        _pipelineCache.TryAdd(cacheKey, result);
        return result;
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

            // Clear pipeline cache since user overrides may have changed
            _pipelineCache.Clear();

            _logger?.LogDebug("Loaded {Count} user category overrides", overrides.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load user category overrides, using defaults");
        }

        // Initialize platform metadata provider (e.g., build .desktop file index on Linux)
        try
        {
            await _metadataProvider.InitializeAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to initialize app metadata provider");
        }

        // Initialize game detection (scan GameConfigStore, launcher registries/manifests)
        try
        {
            await _gameDetectionProvider.InitializeAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to initialize game detection provider");
        }
    }

    /// <summary>
    /// Layered detection pipeline. First non-"Other" result wins.
    /// </summary>
    private string RunPipeline(string exeName, string? exePath, int processId)
    {
        // Layer 1: User override + built-in exe name match
        if (_mappings.TryGetValue(exeName, out var byName))
            return byName;

        // Layer 2: Publisher/company mapping (via FileVersionInfo on Windows)
        if (!string.IsNullOrWhiteSpace(exePath))
        {
            var publisher = _metadataProvider.GetPublisher(exePath);
            if (!string.IsNullOrWhiteSpace(publisher))
            {
                var byPublisher = MatchPublisher(publisher);
                if (byPublisher is not null)
                {
                    _logger?.LogDebug(
                        "Categorized {ExeName} as {Category} via publisher '{Publisher}'",
                        exeName, byPublisher, publisher);
                    return byPublisher;
                }
            }
        }

        // Layer 3: Platform game detection (GameConfigStore, launcher manifests/registries)
        if (!string.IsNullOrWhiteSpace(exePath) && _gameDetectionProvider.IsKnownGame(exePath))
        {
            _logger?.LogDebug(
                "Categorized {ExeName} as {Category} via game detection provider",
                exeName, CategoryGaming);
            return CategoryGaming;
        }

        // Layer 4: OS-level metadata (.desktop files on Linux, AppStream)
        var byOs = _metadataProvider.GetCategoryFromOsMetadata(exeName);
        if (!string.IsNullOrWhiteSpace(byOs))
        {
            _logger?.LogDebug(
                "Categorized {ExeName} as {Category} via OS metadata",
                exeName, byOs);
            return byOs;
        }

        // Layer 5: Path heuristics (installation directory signals)
        if (!string.IsNullOrWhiteSpace(exePath))
        {
            var byPath = MatchPathHeuristic(exePath);
            if (byPath is not null)
            {
                _logger?.LogDebug(
                    "Categorized {ExeName} as {Category} via path heuristic",
                    exeName, byPath);
                return byPath;
            }
        }

        // Layer 6: Parent process attribution
        if (processId > 0)
        {
            var byParent = MatchParentProcess(processId, depth: 0);
            if (byParent is not null)
            {
                _logger?.LogDebug(
                    "Categorized {ExeName} as {Category} via parent process",
                    exeName, byParent);
                return byParent;
            }
        }

        // Layer 7: Fallback
        return CategoryOther;
    }

    private static string? MatchPublisher(string publisher)
    {
        foreach (var (substring, category) in PublisherMappings)
        {
            if (publisher.Contains(substring, StringComparison.OrdinalIgnoreCase))
                return category;
        }

        return null;
    }

    private static string? MatchPathHeuristic(string executablePath)
    {
        foreach (var (segment, category) in PathHeuristics)
        {
            if (executablePath.Contains(segment, StringComparison.OrdinalIgnoreCase))
                return category;
        }

        return null;
    }

    /// <summary>
    /// Walk up the process tree to find a categorizable ancestor.
    /// Skips transparent parents (shells, terminals, system hosts).
    /// Max depth of 3 to avoid infinite loops.
    /// </summary>
    private string? MatchParentProcess(int processId, int depth)
    {
        if (depth >= 3)
            return null;

        var parentName = _metadataProvider.GetParentProcessName(processId);
        if (string.IsNullOrWhiteSpace(parentName))
            return null;

        var normalized = NormalizeName(parentName);

        // Skip transparent parents and keep walking up
        if (TransparentParents.Contains(normalized))
            return null; // Don't recurse further for transparent parents

        // Check if parent has a known category
        if (_mappings.TryGetValue(normalized, out var parentCategory))
            return parentCategory;

        return null;
    }

    private static string NormalizeName(string name)
    {
        var result = Path.GetFileNameWithoutExtension(name);
        return string.IsNullOrWhiteSpace(result) ? name : result;
    }
}
