using System.Runtime.Versioning;
using WireBound.Platform.Windows.Services;

namespace WireBound.Tests.Platform;

[SupportedOSPlatform("windows")]
public class WindowsProgramCatalogTests
{
    [Test]
    public void KnownWindowsComponentsAndInteractiveApps_HaveDistinctRoles()
    {
        if (!OperatingSystem.IsWindows()) return;
        var catalog = new WindowsProgramCatalog();
        var system = catalog.Identify(Path.Combine(Environment.SystemDirectory, "dwm.exe"));
        system.Should().NotBeNull();
        system!.DisplayName.Should().NotBeNullOrWhiteSpace();
        system.IsSystemProcess.Should().BeTrue();
        var explorer = catalog.Identify(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"));
        explorer.Should().NotBeNull();
        explorer!.IsSystemProcess.Should().BeFalse();
        catalog.Identify(Path.Combine(Environment.SystemDirectory, "RuntimeBroker.exe"))!.SeparateInstances.Should().BeTrue();
        foreach (var name in new[] { "osk", "charmap", "cleanmgr", "optionalfeatures" })
            catalog.Identify(Path.Combine(Environment.SystemDirectory, name + ".exe"))!.IsSystemProcess.Should().BeFalse();
        catalog.Identify(Path.Combine(Environment.SystemDirectory, "msinfo32.exe"))!.IsSystemProcess.Should().BeNull();
    }

    [Test]
    public void SimilarNamesAndNeighbouringFolders_AreNotBuiltinWindowsPrograms()
    {
        if (!OperatingSystem.IsWindows()) return;
        var catalog = new WindowsProgramCatalog();
        catalog.Identify("C:/Tools/dwm.exe").Should().BeNull();
        catalog.Identify(Path.Combine(Environment.SystemDirectory + "-copy", "dwm.exe")).Should().BeNull();
        catalog.Identify(Path.Combine(Environment.SystemDirectory, "..", "Temp", "dwm.exe")).Should().BeNull();
        catalog.Identify("dwm.exe").Should().BeNull();
    }

    [Test]
    public void PackagedAppsAndVersionedDefenderPaths_KeepUsefulFallbacks()
    {
        if (!OperatingSystem.IsWindows()) return;
        var catalog = new WindowsProgramCatalog();
        var notepad = catalog.Identify(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "WindowsApps", "Microsoft.WindowsNotepad_0.0.0.0_x64__8wekyb3d8bbwe", "Notepad", "Notepad.exe"));
        notepad!.DisplayName.Should().Be("Notepad");
        notepad.IsSystemProcess.Should().BeFalse();
        var defender = catalog.Identify(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Microsoft", "Windows Defender", "Platform", "0.0.0.0-0", "MsMpEng.exe"));
        defender!.DisplayName.Should().Be("Antimalware Service Executable");
        defender.IsSystemProcess.Should().BeTrue();
    }
}
