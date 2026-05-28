using System.Security.Cryptography;
using System.Text;

namespace WireBound.Platform.Abstract.Helpers;

/// <summary>
/// Cross-platform helpers for deriving stable application identity from an
/// executable path and resolving a friendly display name.
/// </summary>
/// <remarks>
/// <para>
/// Per-app tracking persists records keyed by <c>AppIdentifier</c>, a stable
/// 16-character hex hash of the executable path. Every producer of
/// <see cref="WireBound.Platform.Abstract.Models.ProcessNetworkStats"/> must
/// populate this field through <see cref="ComputeAppIdentifier"/>, otherwise
/// <c>DataPersistenceService.SaveAppStatsAsync</c> silently drops the record
/// (which is how the elevated Windows provider used to produce zero rows).
/// </para>
/// </remarks>
public static class AppIdentity
{
    /// <summary>
    /// Sentinel identifier used when the executable path cannot be resolved.
    /// Records with this identifier still persist (so the user sees an
    /// "Unknown" bucket) but multiple unresolved processes will coalesce.
    /// </summary>
    public const string UnknownIdentifier = "unknown";

    /// <summary>
    /// Computes a stable 16-character hex identifier for an executable path.
    /// Uses SHA-256 over the lower-cased path so the same binary always hashes
    /// to the same value regardless of casing differences in the OS APIs.
    /// </summary>
    public static string ComputeAppIdentifier(string? executablePath)
    {
        if (string.IsNullOrEmpty(executablePath))
        {
            return UnknownIdentifier;
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(executablePath.ToLowerInvariant()));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    /// <summary>
    /// Returns a friendly display name for an application, preferring the
    /// file name (without extension) when an executable path is known and
    /// falling back to the supplied process name otherwise.
    /// </summary>
    public static string ResolveDisplayName(string? executablePath, string processName)
    {
        if (!string.IsNullOrEmpty(executablePath))
        {
            var fileName = Path.GetFileNameWithoutExtension(executablePath);
            if (!string.IsNullOrEmpty(fileName))
            {
                return fileName;
            }
        }

        return processName;
    }
}
