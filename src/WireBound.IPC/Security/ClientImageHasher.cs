using System.Security.Cryptography;

namespace WireBound.IPC.Security;

/// <summary>
/// Computes a stable SHA-256 hash of an executable image on disk.
///
/// <para>
/// Used by both the client (to bind its identity to its own binary) and the
/// server (to independently recompute and verify). Reads the file via a
/// FileShare.Read stream so it can be hashed even while the file is open
/// for execution.
/// </para>
///
/// <para>
/// IMPORTANT: hashing the file at the path on disk does NOT defeat process
/// injection — a malicious DLL loaded into the legitimate WireBound.exe sees
/// the same file. Image-hash binding only defeats:
/// <list type="bullet">
/// <item>"Off-path" copies of WireBound.exe placed in writable directories</item>
/// <item>Same-name binaries planted in the install dir if ACLs ever weakened</item>
/// <item>Partial-upgrade scenarios where two different WireBound binaries co-exist</item>
/// </list>
/// Defeating in-process injection requires <see cref="System.Diagnostics.Process"/>
/// mitigation policies on the main app, not anything done here.
/// </para>
/// </summary>
public static class ClientImageHasher
{
    /// <summary>
    /// Computes the SHA-256 of the file at <paramref name="path"/>.
    /// Throws on I/O errors so callers can fail closed.
    /// </summary>
    public static byte[] HashFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        // FileShare.ReadWrite | Delete lets us hash a binary that is currently
        // executing (Windows holds an exclusive image handle but the file
        // itself remains readable via FILE_SHARE_READ in CreateFile).
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        return SHA256.HashData(stream);
    }
}
