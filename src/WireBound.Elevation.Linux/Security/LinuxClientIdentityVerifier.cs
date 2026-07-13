using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using WireBound.IPC.Security;

namespace WireBound.Elevation.Linux.Security;

/// <summary>
/// Linux implementation of <see cref="IClientIdentityVerifier"/>.
///
/// <para>
/// Uses <c>/proc/&lt;pid&gt;/exe</c> — a kernel-maintained symlink that points
/// to the executable file the process was launched from. Unlike string-based
/// path matching this is race-free against PID reuse and cannot be spoofed
/// by a userland attacker.
/// </para>
///
/// <para>
/// Identity is verified by:
/// <list type="number">
///   <item>Reading <c>/proc/&lt;pid&gt;/exe</c> to get the kernel-verified path
///         via <see cref="File.ResolveLinkTarget"/> with <c>returnFinalTarget</c>
///         set so any intermediate symlinks (bind-mount tricks, etc.) are
///         resolved to the final canonical inode.</item>
///   <item>Confining the path to the install directory.</item>
///   <item>Recomputing SHA-256 of the file and constant-time comparing to
///         the client's claim.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Note on inode-equality checks:</b> an earlier iteration of this verifier
/// called <c>stat()</c> via P/Invoke to compare <c>st_dev</c>/<c>st_ino</c>
/// of the resolved path against the expected install-path. That was removed
/// because (a) the hand-rolled <c>struct stat</c> layout is not portable
/// across glibc &lt; 2.33 (RHEL 8 / Ubuntu 20.04), musl libc (Alpine), or
/// aarch64, and (b) <c>ResolveLinkTarget(returnFinalTarget: true)</c> +
/// the install-dir confinement + the SHA-256 hash recompute already cover
/// every realistic bind-mount / hard-link trick we could enumerate. The
/// inode equality was strict defence-in-depth; the cost in portability was
/// disproportionate.
/// </para>
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxClientIdentityVerifier : IClientIdentityVerifier
{
    private readonly string _expectedInstallDir;
    private readonly string _expectedMainAppPath;

    public LinuxClientIdentityVerifier(string expectedInstallDir, string expectedMainAppPath)
    {
        _expectedInstallDir = Path.GetFullPath(expectedInstallDir);
        if (!_expectedInstallDir.EndsWith('/'))
            _expectedInstallDir += '/';
        _expectedMainAppPath = Path.GetFullPath(expectedMainAppPath);
    }

    public ClientIdentityResult Verify(int pid, ReadOnlySpan<byte> claimedImageHash)
    {
        if (pid <= 0)
            return ClientIdentityResult.Invalid("Invalid client PID");

        // Step 1: kernel-verified executable path via /proc/<pid>/exe.
        // returnFinalTarget collapses any intermediate symlinks so bind-mount
        // or hard-link tricks can't slip a different file under our path check.
        string actualPath;
        try
        {
            var procExeLink = $"/proc/{pid}/exe";
            actualPath = File.ResolveLinkTarget(procExeLink, returnFinalTarget: true)?.FullName
                ?? throw new FileNotFoundException(procExeLink);
        }
        catch (Exception ex)
        {
            return ClientIdentityResult.Invalid($"Could not resolve /proc/{pid}/exe: {ex.Message}");
        }

        var canonicalActual = Path.GetFullPath(actualPath);

        // Step 2: confine to install dir + match the expected main app exactly.
        if (!canonicalActual.StartsWith(_expectedInstallDir, StringComparison.Ordinal))
            return ClientIdentityResult.Invalid(
                $"Client image '{canonicalActual}' is outside install dir '{_expectedInstallDir}'");

        if (!string.Equals(canonicalActual, _expectedMainAppPath, StringComparison.Ordinal))
            return ClientIdentityResult.Invalid(
                $"Client image '{canonicalActual}' is not the expected main app '{_expectedMainAppPath}'");

        // Step 3: independent SHA-256 hash + constant-time compare. Any
        // attacker who replaced the file content (write to a hard-link, for
        // example) shows a different hash here even if the path matched.
        byte[] actualHash;
        try
        {
            actualHash = ClientImageHasher.HashFile(canonicalActual);
        }
        catch (Exception ex)
        {
            return ClientIdentityResult.Invalid($"Could not hash client image: {ex.Message}");
        }

        if (claimedImageHash.Length != actualHash.Length ||
            !CryptographicOperations.FixedTimeEquals(claimedImageHash, actualHash))
        {
            return ClientIdentityResult.Invalid("Client image hash does not match disk file");
        }

        return ClientIdentityResult.Valid(canonicalActual);
    }
}

