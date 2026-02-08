using System.Diagnostics;
using System.Runtime.Versioning;
using Serilog;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Windows.Services;

/// <summary>
/// Windows implementation of network cost detection using the NetworkListManager COM API.
/// Falls back to assuming unmetered if detection fails.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsNetworkCostProvider : INetworkCostProvider
{
    public Task<bool> IsMeteredAsync()
    {
        try
        {
            // Use PowerShell to query the connection cost via WinRT interop
            // This avoids adding a Windows TFM dependency to the platform project
            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = "-NoProfile -Command \"[Windows.Networking.Connectivity.NetworkInformation,Windows.Networking.Connectivity,ContentType=WindowsRuntime] | Out-Null; $p = [Windows.Networking.Connectivity.NetworkInformation]::GetInternetConnectionProfile(); if ($p) { $c = $p.GetConnectionCost(); $c.NetworkCostType.ToString() } else { 'Unknown' }\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null) return Task.FromResult(false);

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);

            // NetworkCostType: Unrestricted, Fixed, Variable, Unknown
            var isMetered = output is "Fixed" or "Variable";
            return Task.FromResult(isMetered);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to detect metered network status on Windows");
            return Task.FromResult(false);
        }
    }
}
