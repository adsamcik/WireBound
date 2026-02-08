using System.Diagnostics;
using System.Runtime.Versioning;
using Serilog;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Linux.Services;

/// <summary>
/// Linux implementation of network cost detection using nmcli.
/// Checks the GENERAL.METERED property of the active connection.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxNetworkCostProvider : INetworkCostProvider
{
    public async Task<bool> IsMeteredAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "nmcli",
                Arguments = "-t -f GENERAL.METERED dev show",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null) return false;

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            return output.Contains("yes", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to detect metered network status on Linux (nmcli not available?)");
            return false;
        }
    }
}
