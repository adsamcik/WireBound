using System.Collections.Concurrent;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Linux.Services;

/// <summary>
/// Linux implementation of IAppMetadataProvider.
/// Parses .desktop files for freedesktop.org categories and /proc for parent process info.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxAppMetadataProvider : IAppMetadataProvider
{
    private readonly ILogger<LinuxAppMetadataProvider>? _logger;

    /// <summary>
    /// Maps executable name (lowercase, no extension) → WireBound category.
    /// Built from .desktop files at initialization.
    /// </summary>
    private volatile IReadOnlyDictionary<string, string> _desktopCategories =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] DesktopFileDirs =
    [
        "/usr/share/applications",
        "/usr/local/share/applications",
        "/var/lib/flatpak/exports/share/applications",
        "/var/lib/snapd/desktop/applications"
    ];

    /// <summary>
    /// Maps freedesktop.org main categories to WireBound categories.
    /// See: https://specifications.freedesktop.org/menu-spec/latest/apa.html
    /// </summary>
    private static readonly Dictionary<string, string> FreedesktopCategoryMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Web Browsers
        ["WebBrowser"] = "Web Browsers",

        // Development Tools
        ["Development"] = "Development Tools",
        ["IDE"] = "Development Tools",
        ["TextEditor"] = "Development Tools",
        ["Debugger"] = "Development Tools",
        ["RevisionControl"] = "Development Tools",

        // Communication
        ["InstantMessaging"] = "Communication",
        ["Chat"] = "Communication",
        ["VideoConference"] = "Communication",
        ["IRCClient"] = "Communication",
        ["Telephony"] = "Communication",
        ["Email"] = "Communication",
        ["ContactManagement"] = "Communication",

        // Media
        ["AudioVideo"] = "Media",
        ["Audio"] = "Media",
        ["Video"] = "Media",
        ["Music"] = "Media",
        ["Player"] = "Media",
        ["Recorder"] = "Media",
        ["Photography"] = "Media",
        ["Graphics"] = "Media",
        ["2DGraphics"] = "Media",
        ["3DGraphics"] = "Media",
        ["RasterGraphics"] = "Media",
        ["VectorGraphics"] = "Media",

        // Gaming
        ["Game"] = "Gaming",
        ["ActionGame"] = "Gaming",
        ["AdventureGame"] = "Gaming",
        ["ArcadeGame"] = "Gaming",
        ["BoardGame"] = "Gaming",
        ["CardGame"] = "Gaming",
        ["Emulator"] = "Gaming",
        ["LogicGame"] = "Gaming",
        ["RolePlaying"] = "Gaming",
        ["Shooter"] = "Gaming",
        ["Simulation"] = "Gaming",
        ["SportsGame"] = "Gaming",
        ["StrategyGame"] = "Gaming",

        // System Services
        ["System"] = "System Services",
        ["Settings"] = "System Services",
        ["HardwareSettings"] = "System Services",
        ["PackageManager"] = "System Services",
        ["Monitor"] = "System Services",
        ["Security"] = "System Services",
        ["Accessibility"] = "System Services",
        ["TerminalEmulator"] = "Development Tools", // Terminals → Dev Tools like WireBound convention

        // Office
        ["Office"] = "Office",
        ["Calendar"] = "Office",
        ["ProjectManagement"] = "Office",
        ["Presentation"] = "Office",
        ["Spreadsheet"] = "Office",
        ["WordProcessor"] = "Office",
        ["Dictionary"] = "Office",
        ["Finance"] = "Office",

        // Network tools that aren't browsers
        ["Network"] = "Communication",
        ["FileTransfer"] = "Other",
        ["P2P"] = "Other",
    };

    public LinuxAppMetadataProvider(ILogger<LinuxAppMetadataProvider>? logger = null)
    {
        _logger = logger;
    }

    public string? GetPublisher(string executablePath) => null;

    public string? GetCategoryFromOsMetadata(string executableName)
    {
        if (string.IsNullOrWhiteSpace(executableName))
            return null;

        var name = Path.GetFileNameWithoutExtension(executableName);
        return _desktopCategories.TryGetValue(name, out var category) ? category : null;
    }

    public string? GetParentProcessName(int processId)
    {
        if (processId <= 0)
            return null;

        try
        {
            var statusPath = $"/proc/{processId}/status";
            if (!File.Exists(statusPath))
                return null;

            foreach (var line in File.ReadLines(statusPath))
            {
                if (!line.StartsWith("PPid:", StringComparison.Ordinal))
                    continue;

                var ppidStr = line.AsSpan(5).Trim();
                if (!int.TryParse(ppidStr, out var ppid) || ppid <= 1)
                    return null;

                var commPath = $"/proc/{ppid}/comm";
                if (!File.Exists(commPath))
                    return null;

                return File.ReadAllText(commPath).Trim();
            }
        }
        catch
        {
            // Process may have exited or permission denied
        }

        return null;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await Task.Run(() => BuildDesktopCategoryIndex(), cancellationToken);
    }

    private void BuildDesktopCategoryIndex()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var processedCount = 0;

        foreach (var dir in DesktopFileDirs)
        {
            if (!Directory.Exists(dir))
                continue;

            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*.desktop"))
                {
                    try
                    {
                        ParseDesktopFile(file, result);
                        processedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogTrace(ex, "Failed to parse .desktop file: {File}", file);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Failed to enumerate .desktop files in {Dir}", dir);
            }
        }

        // Also scan user-local .desktop files
        var userDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "share", "applications");

        if (Directory.Exists(userDir))
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(userDir, "*.desktop"))
                {
                    try
                    {
                        ParseDesktopFile(file, result);
                        processedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogTrace(ex, "Failed to parse user .desktop file: {File}", file);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Failed to enumerate user .desktop files in {Dir}", userDir);
            }
        }

        _desktopCategories = result;
        _logger?.LogDebug(
            "Built .desktop category index: {MappedCount} apps mapped from {ProcessedCount} .desktop files",
            result.Count, processedCount);
    }

    private static void ParseDesktopFile(string filePath, Dictionary<string, string> result)
    {
        string? execValue = null;
        string? categoriesValue = null;
        string? type = null;
        var inDesktopEntry = false;

        foreach (var line in File.ReadLines(filePath))
        {
            var trimmed = line.AsSpan().Trim();

            if (trimmed.IsEmpty || trimmed[0] == '#')
                continue;

            // Section header
            if (trimmed[0] == '[')
            {
                if (inDesktopEntry)
                    break; // We've passed [Desktop Entry], stop

                inDesktopEntry = trimmed.Equals("[Desktop Entry]", StringComparison.Ordinal);
                continue;
            }

            if (!inDesktopEntry)
                continue;

            if (trimmed.StartsWith("Type=", StringComparison.Ordinal))
                type = trimmed[5..].ToString().Trim();
            else if (trimmed.StartsWith("Exec=", StringComparison.Ordinal))
                execValue = trimmed[5..].ToString().Trim();
            else if (trimmed.StartsWith("Categories=", StringComparison.Ordinal))
                categoriesValue = trimmed[11..].ToString().Trim();
        }

        if (type != "Application" || string.IsNullOrEmpty(execValue) || string.IsNullOrEmpty(categoriesValue))
            return;

        // Extract executable name from Exec= value
        // Exec can be: "/usr/bin/firefox %u", "env VAR=val /usr/bin/app", "flatpak run org.app", etc.
        var exeName = ExtractExeNameFromExecLine(execValue);
        if (string.IsNullOrEmpty(exeName))
            return;

        // Find best matching WireBound category from the freedesktop categories
        var wireBoundCategory = MapFreedesktopCategories(categoriesValue);
        if (wireBoundCategory is not null)
        {
            result.TryAdd(exeName, wireBoundCategory);
        }
    }

    private static string? ExtractExeNameFromExecLine(string execLine)
    {
        // Remove field codes (%u, %f, %F, %U, etc.)
        var parts = execLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            if (part.StartsWith('%'))
                continue;
            if (part.Contains('=')) // env VAR=val
                continue;
            if (part is "env" or "flatpak" or "snap")
                continue;
            if (part is "run") // "flatpak run ..."
                continue;

            // This should be the actual executable path or name
            var name = Path.GetFileNameWithoutExtension(part);
            if (!string.IsNullOrEmpty(name))
                return name;
        }

        return null;
    }

    private static string? MapFreedesktopCategories(string categoriesValue)
    {
        // Categories is semicolon-separated: "Network;WebBrowser;GTK;"
        // Try more specific categories first, then fall back to broader ones
        string? broadCategory = null;

        foreach (var cat in categoriesValue.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = cat.Trim();
            if (FreedesktopCategoryMap.TryGetValue(trimmed, out var mapped))
            {
                // Prefer more specific matches (subcategories tend to come after main categories)
                if (broadCategory is null ||
                    trimmed is "WebBrowser" or "IDE" or "TerminalEmulator" or "InstantMessaging"
                        or "VideoConference" or "Email" or "Game")
                {
                    broadCategory = mapped;
                }
            }
        }

        return broadCategory;
    }
}
