using System.Runtime.Versioning;
using System.Text.Json;
using WireBound.Platform.Abstract.Helpers;
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
        return Path.GetFileNameWithoutExtension(processName).ToLowerInvariant() switch
        {
            "vmmem" => new("vm-host", "Virtual machine host", "Process name: Vmmem"),
            "vmmemwsl" => new("wsl-host", "WSL utility VM host", "Process name: VmmemWSL"),
            "vmwp" => new("hyperv-host", "Hyper-V worker", "Process name: vmwp"),
            "vmware-vmx" => new("vmware-host", "VMware worker", "Process name: vmware-vmx"),
            "virtualboxvm" => new("virtualbox-host", "VirtualBox worker", "Process name: VirtualBoxVM"),
            "vboxheadless" => new("virtualbox-host", "VirtualBox worker", "Process name: VBoxHeadless"),
            "qemu-system-x86_64" or "qemu-system-aarch64" => new("qemu-host", "QEMU worker", $"Process name: {processName}"),
            _ => null
        };
    }

    public async Task<IReadOnlyList<MemoryWorkload>> GetVirtualMachinesAsync(CancellationToken cancellationToken = default)
    {
        var script = "[Console]::OutputEncoding=[System.Text.UTF8Encoding]::new($false); $ErrorActionPreference='Stop'; try { @(Get-VM | Where-Object { $_.State -ne 'Off' } | Select-Object @{n='Id';e={$_.Id.ToString()}},Name,MemoryAssigned) | ConvertTo-Json -Compress } catch { exit 1 }";
        var result = await LocalCommandRunner.RunAsync("powershell.exe",
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", script], cancellationToken);
        var rows = new List<MemoryWorkload>();
        if (result is null)
            rows.Add(new("hyperv-status", "Hyper-V guests", null, "Unavailable",
                "Guest names and assigned memory require the Hyper-V PowerShell module and permission to read local VMs."));
        else if (!string.IsNullOrWhiteSpace(result))
        {
            try
            {
                using var document = JsonDocument.Parse(result);
                var guests = document.RootElement.ValueKind == JsonValueKind.Array
                    ? document.RootElement.EnumerateArray().ToArray() : [document.RootElement];
                foreach (var guest in guests)
                    rows.Add(new("hyperv:" + guest.GetProperty("Id").GetString(),
                        guest.GetProperty("Name").GetString() ?? "Hyper-V guest",
                        guest.GetProperty("MemoryAssigned").GetInt64(), "Assigned to VM",
                        "Hyper-V Get-VM · assigned guest memory, outside host physical totals. Guest process memory requires an in-guest collector."));
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
            {
                rows.Add(new("hyperv-status", "Hyper-V guests", null, "Unavailable", "Hyper-V returned an unreadable sample. Refresh to retry."));
            }
        }

        // Listing running distributions does not start or enter a guest.
        var wsl = await LocalCommandRunner.RunAsync("wsl.exe", ["--list", "--running", "--quiet"], cancellationToken,
            detectUtf16: true);
        if (wsl is not null)
            foreach (var name in wsl.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                rows.Add(new("wsl:" + name, name.Equals("rancher-desktop", StringComparison.OrdinalIgnoreCase) ? "Rancher Desktop VM" : name,
                    null, "WSL distribution",
                    "Running WSL distribution. Distributions share the WSL utility VM; per-distribution physical memory is not independently measured.", Summary: name));
        else
            rows.Add(new("wsl-status", "WSL distributions", null, "Unavailable",
                "Running distribution names could not be read. Check that WSL is installed and available to this account, then refresh."));
        return rows;
    }
}
