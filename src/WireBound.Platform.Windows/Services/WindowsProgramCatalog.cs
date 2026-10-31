using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.Versioning;
using WireBound.Platform.Abstract.Models;

namespace WireBound.Platform.Windows.Services;

/// <summary>Windows file descriptions with a small fallback catalogue. Names alone never establish a built-in program.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsProgramCatalog
{
    private readonly ConcurrentDictionary<string, WindowsProgramInfo> _cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, WindowsProgramInfo> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["explorer"] = new("Windows Explorer", false),
        ["taskmgr"] = new("Task Manager", false),
        ["notepad"] = new("Notepad", false),
        ["mspaint"] = new("Paint", false),
        ["calc"] = new("Calculator", false),
        ["CalculatorApp"] = new("Calculator", false),
        ["SnippingTool"] = new("Snipping Tool", false),
        ["SystemSettings"] = new("Settings", false),
        ["WindowsTerminal"] = new("Windows Terminal", false),
        ["cmd"] = new("Windows Command Processor", false),
        ["powershell"] = new("Windows PowerShell", false),
        ["mmc"] = new("Microsoft Management Console", false),
        ["regedit"] = new("Registry Editor", false),
        ["mstsc"] = new("Remote Desktop Connection", false),
        ["control"] = new("Control Panel", false),
        ["msiexec"] = new("Windows Installer", false),
        ["osk"] = new("On-Screen Keyboard", false),
        ["charmap"] = new("Character Map", false),
        ["cleanmgr"] = new("Disk Cleanup", false),
        ["optionalfeatures"] = new("Windows Features", false),
        ["dwm"] = new("Desktop Window Manager", true),
        ["csrss"] = new("Client Server Runtime Process", true),
        ["wininit"] = new("Windows Start-up Application", true),
        ["winlogon"] = new("Windows Logon Application", true),
        ["lsass"] = new("Local Security Authority Process", true),
        ["services"] = new("Services and Controller app", true),
        ["smss"] = new("Windows Session Manager", true),
        ["fontdrvhost"] = new("Usermode Font Driver Host", true),
        ["spoolsv"] = new("Print Spooler", true),
        ["SearchIndexer"] = new("Windows Search Indexer", true),
        ["SearchHost"] = new("Windows Search", true),
        ["SearchApp"] = new("Windows Search", true),
        ["StartMenuExperienceHost"] = new("Start", true),
        ["ShellExperienceHost"] = new("Windows Shell Experience Host", true),
        ["TextInputHost"] = new("Windows Input Experience", true),
        ["LockApp"] = new("Windows Lock Screen", true),
        ["RuntimeBroker"] = new("Runtime Broker", true, true),
        ["dllhost"] = new("COM Surrogate", true, true),
        ["taskhostw"] = new("Host Process for Windows Tasks", true, true),
        ["conhost"] = new("Console Window Host", true, true),
        ["OpenConsole"] = new("Console Window Host", true, true),
        ["backgroundTaskHost"] = new("Background Task Host", true, true),
        ["ApplicationFrameHost"] = new("Application Frame Host", true, true),
        ["rundll32"] = new("Windows DLL Host", true, true),
        ["audiodg"] = new("Windows Audio Device Graph Isolation", true),
        ["sihost"] = new("Shell Infrastructure Host", true),
        ["dasHost"] = new("Device Association Framework Provider Host", true),
        ["smartscreen"] = new("Microsoft Defender SmartScreen", true),
        ["SecurityHealthService"] = new("Windows Security Service", true),
        ["SecurityHealthSystray"] = new("Windows Security notification icon", true),
        ["MsMpEng"] = new("Antimalware Service Executable", true),
        ["NisSrv"] = new("Microsoft Defender Network Inspection Service", true),
        ["MpDefenderCoreService"] = new("Microsoft Defender Core Service", true),
        ["TrustedInstaller"] = new("Windows Modules Installer", true),
        ["TiWorker"] = new("Windows Modules Installer Worker", true),
        ["MoUsoCoreWorker"] = new("Windows Update Worker", true)
    };

    private static readonly string Windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static readonly string ProgramFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    private static readonly string ProgramData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    internal WindowsProgramInfo? Identify(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return null;
        string fullPath;
        try { fullPath = Path.GetFullPath(path); }
        catch (ArgumentException) { return null; }
        if (!IsWindowsLocation(fullPath)) return null;
        if (_cache.TryGetValue(fullPath, out var cached)) return cached;
        var name = Path.GetFileNameWithoutExtension(fullPath);
        Known.TryGetValue(name, out var known);
        WindowsProgramInfo? program = known;
        try
        {
            var version = FileVersionInfo.GetVersionInfo(fullPath);
            if (string.Equals(version.CompanyName, "Microsoft Corporation", StringComparison.OrdinalIgnoreCase)
                && version.ProductName?.Contains("Windows", StringComparison.OrdinalIgnoreCase) == true
                && !string.IsNullOrWhiteSpace(version.FileDescription))
                program = new(version.FileDescription.Trim(), known?.IsSystemProcess, known?.SeparateInstances ?? false);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (System.ComponentModel.Win32Exception) { }
        if (program is not null)
        {
            if (_cache.Count >= 512) _cache.Clear();
            _cache[fullPath] = program;
        }
        return program;
    }

    private static bool IsWindowsLocation(string path)
    {
        static bool Under(string candidate, string root) => root.Length > 0
            && candidate.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        if (Windows.Length > 0)
        {
            if (string.Equals(Path.GetDirectoryName(path), Windows, StringComparison.OrdinalIgnoreCase)) return true;
            foreach (var directory in new[] { "System32", "SysWOW64", "servicing", "WinSxS", "ImmersiveControlPanel", "UUS" })
                if (Under(path, Path.Combine(Windows, directory))) return true;
            if (Under(path, Path.Combine(Windows, "SystemApps")))
            {
                var folder = Path.GetRelativePath(Path.Combine(Windows, "SystemApps"), path).Split(Path.DirectorySeparatorChar)[0];
                return folder.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase)
                    || folder.StartsWith("MicrosoftWindows.", StringComparison.OrdinalIgnoreCase)
                    || folder.StartsWith("ShellExperienceHost_", StringComparison.OrdinalIgnoreCase);
            }
        }
        if (ProgramData.Length > 0 && Under(path, Path.Combine(ProgramData, "Microsoft", "Windows Defender", "Platform"))) return true;
        if (ProgramFiles.Length == 0) return false;
        if (Under(path, Path.Combine(ProgramFiles, "Windows Defender"))) return true;
        if (!Under(path, Path.Combine(ProgramFiles, "WindowsApps"))) return false;
        var package = Path.GetRelativePath(Path.Combine(ProgramFiles, "WindowsApps"), path).Split(Path.DirectorySeparatorChar)[0];
        return new[] { "Microsoft.WindowsNotepad_", "Microsoft.Paint_", "Microsoft.WindowsCalculator_", "Microsoft.ScreenSketch_", "Microsoft.WindowsTerminal_" }
            .Any(prefix => package.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}
