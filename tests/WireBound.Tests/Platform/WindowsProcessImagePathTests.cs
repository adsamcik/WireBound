using System.Diagnostics;
using System.Runtime.Versioning;
using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Windows.Services;

namespace WireBound.Tests.Platform;

[SupportedOSPlatform("windows")]
public class WindowsProcessImagePathTests
{
    [Test]
    public void NativeDevicePaths_RespectVolumeBoundariesAndActualDriveMappings()
    {
        var drives = new Dictionary<string, string> { [@"\Device\HarddiskVolume3"] = "C:", [@"\Device\HarddiskVolume30"] = "G:" };
        WindowsProcessImagePath.ToDosPath(@"\Device\HarddiskVolume30\Windows\System32\dwm.exe", drives).Should().Be(@"G:\Windows\System32\dwm.exe");
        WindowsProcessImagePath.ToDosPath(@"\device\harddiskvolume3\Windows\explorer.exe", drives).Should().Be(@"C:\Windows\explorer.exe");
        WindowsProcessImagePath.ToDosPath(@"\Device\HarddiskVolume31\dwm.exe", drives).Should().BeNull();
    }

    [Test]
    public void NativeImageQuery_ReadsCurrentProcessWithoutAnOpenProcessHandle()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var process = Process.GetCurrentProcess();
        var sample = new ProcessResourceData { ProcessId = process.Id, StartMarker = process.StartTime.ToUniversalTime().Ticks };
        var paths = WindowsProcessImagePath.Capture([sample], default);
        paths[process.Id].Should().BeEquivalentTo(Environment.ProcessPath);
    }
}
