using System.Runtime.Versioning;
using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Linux.Services;

[SupportedOSPlatform("linux")]
public sealed class LinuxWorkloadHostProvider : IWorkloadHostProvider
{
    public WorkloadHost? IdentifyHost(string processName, string executablePath)
    {
        return processName.ToLowerInvariant() switch
        {
            "qemu-system-x86_64" or "qemu-system-aarch64" =>
                new("qemu-host", "QEMU worker", $"Process name: {processName}"),
            "virtualboxvm" => new("virtualbox-host", "VirtualBox worker", "Process name: VirtualBoxVM"),
            _ => null
        };
    }
}
