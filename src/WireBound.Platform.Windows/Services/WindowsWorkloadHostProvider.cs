using System.Runtime.Versioning;
using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Windows.Services;

[SupportedOSPlatform("windows")]
public sealed class WindowsWorkloadHostProvider : IWorkloadHostProvider
{
    public WorkloadHost? IdentifyHost(string processName, string executablePath)
    {
        // Name-only matches are intentionally only candidate host observations.
        // They do not identify a guest VM or give an exclusive RAM figure.
        return processName.ToLowerInvariant() switch
        {
            "vmmemwsl" => new("wsl-host", "WSL utility VM host", "Process name: VmmemWSL"),
            "vmwp" => new("hyperv-host", "Hyper-V worker", "Process name: vmwp"),
            "vmware-vmx" => new("vmware-host", "VMware worker", "Process name: vmware-vmx"),
            "virtualboxvm" => new("virtualbox-host", "VirtualBox worker", "Process name: VirtualBoxVM"),
            _ => null
        };
    }
}
