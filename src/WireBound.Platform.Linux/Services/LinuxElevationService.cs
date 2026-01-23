using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Linux.Services;

/// <summary>
/// Linux implementation of <see cref="IElevationService"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Current Status:</b> Elevation is not yet implemented for Linux.
/// The application will report that elevation is not supported and return
/// appropriate results when elevation is requested.
/// </para>
/// <para>
/// <b>Future Implementation Options:</b>
/// <list type="bullet">
/// <item>
/// <b>pkexec:</b> PolicyKit-based execution that shows a graphical
/// authentication dialog. Recommended for desktop environments.
/// Example: <c>pkexec /path/to/wirebound</c>
/// </item>
/// <item>
/// <b>sudo with askpass:</b> Using SUDO_ASKPASS to prompt for password
/// in a graphical dialog. Requires configuration.
/// </item>
/// <item>
/// <b>Helper Process (Recommended):</b> A separate elevated daemon process
/// that handles privileged operations via IPC. This is the most secure
/// approach and is documented in docs/DESIGN_PER_ADDRESS_TRACKING.md.
/// </item>
/// </list>
/// </para>
/// <para>
/// <b>Checking for Root:</b>
/// On Linux, elevation means running as root (uid 0). This can be checked
/// with <c>geteuid() == 0</c> or by reading /proc/self/status.
/// </para>
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class LinuxElevationService : IElevationService
{
    private readonly ILogger<LinuxElevationService>? _logger;
    private readonly Lazy<bool> _isElevated;

    public LinuxElevationService(ILogger<LinuxElevationService>? logger = null)
    {
        _logger = logger;
        _isElevated = new Lazy<bool>(CheckIsElevated);
    }

    /// <inheritdoc />
    public bool IsElevated => _isElevated.Value;

    /// <inheritdoc />
    public bool RequiresElevation => !IsElevated;

    /// <inheritdoc />
    /// <remarks>
    /// Currently returns false. Linux elevation via pkexec is planned for future releases.
    /// </remarks>
    public bool IsElevationSupported => false;

    /// <inheritdoc />
    public Task<ElevationResult> TryElevateAsync()
    {
        _logger?.LogInformation("Elevation requested on Linux - not yet implemented");

        // TODO: Implement pkexec-based elevation
        // The implementation would:
        // 1. Check if pkexec is available
        // 2. Start a new process with: pkexec /path/to/wirebound
        // 3. Handle PolicyKit authentication
        //
        // See docs/DESIGN_PER_ADDRESS_TRACKING.md for the preferred helper process approach.

        _logger?.LogWarning(
            "Linux elevation is not yet implemented. Per-process network monitoring " +
            "requires running as root. Consider starting with: sudo ./WireBound.Avalonia");

        return Task.FromResult(ElevationResult.NotSupported());
    }

    /// <inheritdoc />
    public void ExitAfterElevation()
    {
        _logger?.LogInformation("ExitAfterElevation called on Linux (no-op since elevation not supported)");
        // No-op since elevation isn't supported yet
    }

    /// <inheritdoc />
    public bool RequiresElevationFor(ElevatedFeature feature)
    {
        return feature switch
        {
            // Per-process byte tracking requires root for eBPF/raw access
            ElevatedFeature.PerProcessNetworkMonitoring => !IsElevated,
            // Raw socket capture always requires root
            ElevatedFeature.RawSocketCapture => !IsElevated,
            _ => false
        };
    }

    /// <summary>
    /// Checks if the current process is running as root.
    /// </summary>
    private static bool CheckIsElevated()
    {
        try
        {
            // On Linux, root has uid 0
            // Environment.UserName would return "root" but checking uid is more reliable
            var uid = GetEffectiveUserId();
            return uid == 0;
        }
        catch
        {
            // If we can't determine elevation status, assume not elevated
            return false;
        }
    }

    /// <summary>
    /// Gets the effective user ID using /proc/self/status.
    /// </summary>
    private static int GetEffectiveUserId()
    {
        try
        {
            // Read /proc/self/status which contains Uid line
            // Format: Uid: real effective saved filesystem
            var lines = File.ReadAllLines("/proc/self/status");
            foreach (var line in lines)
            {
                if (line.StartsWith("Uid:", StringComparison.Ordinal))
                {
                    var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && int.TryParse(parts[1], out var euid))
                    {
                        return euid;
                    }
                }
            }
        }
        catch
        {
            // Fallback to checking username
            if (string.Equals(Environment.UserName, "root", StringComparison.Ordinal))
            {
                return 0;
            }
        }

        return -1; // Unknown, assume not root
    }
}
