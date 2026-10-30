using System.Runtime.Versioning;
using System.Globalization;
using WireBound.Platform.Abstract.Helpers;
using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Linux.Services;

[SupportedOSPlatform("linux")]
public sealed class LinuxWorkloadHostProvider : IWorkloadHostProvider
{
    public WorkloadHost? IdentifyHost(string processName, string executablePath)
    {
        var name = string.IsNullOrEmpty(executablePath) ? processName : Path.GetFileName(executablePath);
        return name.ToLowerInvariant() switch
        {
            "qemu-system-x86_64" or "qemu-system-aarch64" =>
                new("qemu-host", "QEMU worker", $"Process name: {processName}"),
            "virtualboxvm" or "vboxheadless" => new("virtualbox-host", "VirtualBox worker", $"Executable: {name}"),
            _ => null
        };
    }

    public async Task<IReadOnlyList<MemoryWorkload>> GetVirtualMachinesAsync(CancellationToken cancellationToken = default)
    {
        var outputs = await Task.WhenAll(new[] { "qemu:///system", "qemu:///session" }.Select(async connection =>
            (Connection: connection, Output: await LocalCommandRunner.RunAsync("virsh",
                ["--readonly", "--connect", connection, "domstats", "--list-active", "--balloon", "--raw"], cancellationToken))));
        if (outputs.All(o => o.Output is null))
            return [new("libvirt-status", "libvirt guests", null, "Unavailable",
                "Named guest memory requires virsh and permission to read the local libvirt connection. Host processes remain listed when visible.")];
        var rows = new List<MemoryWorkload>();
        foreach (var (connection, output) in outputs)
        {
            if (output is null)
            {
                rows.Add(new($"libvirt-status:{connection}", $"libvirt {connection} guests", null, "Unavailable",
                    "This local libvirt scope could not be read. Check connection permissions, then refresh."));
                continue;
            }
            string? name = null;
            foreach (var line in output.Split('\n', StringSplitOptions.TrimEntries))
            {
                if (line.StartsWith("Domain: '", StringComparison.Ordinal)) name = line[9..].TrimEnd('\'');
                if (name is not null && line.StartsWith("balloon.current=", StringComparison.Ordinal)
                    && long.TryParse(line[16..], NumberStyles.None, CultureInfo.InvariantCulture, out var kib)
                    && kib <= long.MaxValue / 1024)
                    rows.Add(new($"libvirt:{connection}:{name}", name, kib * 1024, "Assigned to VM",
                        $"libvirt {connection} balloon.current · guest allocation, outside host physical totals. Guest process memory requires an in-guest collector."));
            }
        }
        return rows;
    }
}
