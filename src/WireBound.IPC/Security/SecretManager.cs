using System.Runtime.Versioning;
using System.Security;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;

namespace WireBound.IPC.Security;

/// <summary>
/// Manages the shared HMAC secret used for IPC authentication.
/// Handles secure generation, storage with OS-level file protection, and loading.
/// </summary>
public static class SecretManager
{
    private const int SecretLength = 32;
    private const string SecretFileName = ".elevation-secret";

    /// <summary>
    /// Gets the platform-appropriate path for the secret file.
    /// Windows: %LOCALAPPDATA%\WireBound\.elevation-secret
    /// Linux:   ~/.local/share/WireBound/.elevation-secret
    /// </summary>
    public static string GetSecretFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "WireBound", SecretFileName);
    }

    /// <summary>
    /// Gets the secret file path rooted in a specific user's home directory (Linux).
    /// Used by the elevated helper to write the secret into the client user's home.
    /// Validates and canonicalizes the path to prevent directory traversal attacks.
    /// </summary>
    public static string GetSecretFilePathForUser(string userHomeDir)
    {
        var canonical = Path.GetFullPath(userHomeDir);

        // Resolve symlinks in the home directory path itself to prevent
        // /home/alice -> /etc style attacks
        if (Directory.Exists(canonical))
        {
            var dirInfo = new DirectoryInfo(canonical);
            if (dirInfo.LinkTarget is not null)
            {
                // Resolve the final target and re-validate
                canonical = Path.GetFullPath(dirInfo.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? canonical);
            }
        }

        // Reject paths that don't look like real home directories
        if (!canonical.StartsWith("/home/", StringComparison.Ordinal) &&
            !canonical.Equals("/root", StringComparison.Ordinal))
        {
            throw new SecurityException(
                $"Invalid home directory for secret storage: {canonical}. " +
                "Must be under /home/ or /root.");
        }

        return Path.Combine(canonical, ".local", "share", "WireBound", SecretFileName);
    }

    /// <summary>
    /// Generates a new cryptographically secure secret and writes it to disk
    /// with restrictive file permissions.
    /// </summary>
    /// <param name="customBasePath">
    /// Optional directory override for the secret file. When null the
    /// platform-default location returned by <see cref="GetSecretFilePath"/> is used.
    /// Pass a user-specific directory on Linux when the helper (root) must write
    /// into the client user's home.
    /// </param>
    public static byte[] GenerateAndStore(string? customBasePath = null)
    {
        var secret = RandomNumberGenerator.GetBytes(SecretLength);
        var path = customBasePath != null
            ? Path.Combine(customBasePath, SecretFileName)
            : GetSecretFilePath();

        var dir = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        if (OperatingSystem.IsWindows())
        {
            WriteSecretWindows(path, secret);
        }
        else if (OperatingSystem.IsLinux())
        {
            WriteSecretLinux(path, secret);
        }
        else
        {
            File.WriteAllBytes(path, secret);
        }

        return secret;
    }

    /// <summary>
    /// Loads the secret from disk. Returns null if the file doesn't exist or is invalid.
    /// Rejects symlinks/reparse points to prevent redirection attacks.
    /// </summary>
    public static byte[]? Load()
    {
        var path = GetSecretFilePath();
        if (!File.Exists(path))
            return null;

        // Reject symlinks/reparse points on the read path (mirrors the write-path checks)
        var fileInfo = new FileInfo(path);
        if (fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new SecurityException($"Secret path is a reparse point: {path}");

        var secret = File.ReadAllBytes(path);
        return secret.Length == SecretLength ? secret : null;
    }

    /// <summary>
    /// Deletes the secret file from disk.
    /// </summary>
    public static void Delete()
    {
        var path = GetSecretFilePath();
        if (File.Exists(path))
        {
            // Overwrite with zeros before deleting
            var zeros = new byte[SecretLength];
            File.WriteAllBytes(path, zeros);
            File.Delete(path);
        }
    }

    /// <summary>
    /// Creates and writes the secret file using restrictive ACL from the first write.
    /// Rejects the path if it is already a reparse point (symlink/junction) to prevent
    /// symlink-redirect attacks.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static void WriteSecretWindows(string path, byte[] secret)
    {
        var fileInfo = new FileInfo(path);
        if (fileInfo.Exists && fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new SecurityException($"Secret path is a reparse point: {path}");

        var directoryPath = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Secret directory path is unavailable.");
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        security.AddAccessRule(new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        new DirectoryInfo(directoryPath).SetAccessControl(security);

        using var stream = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None
        });
        stream.Write(secret);
    }

    /// <summary>
    /// Creates and writes the secret file with owner-only permissions (0600).
    /// Rejects symlinks to prevent redirection attacks when writing as root
    /// into a user-controlled directory. Checks both existing and dangling symlinks.
    /// </summary>
    [SupportedOSPlatform("linux")]
    private static void WriteSecretLinux(string path, byte[] secret)
    {
        // Check for symlinks — must detect BOTH existing and dangling symlinks.
        // File.Exists() returns false for dangling symlinks, so we use FileInfo
        // with a direct attribute check via the path entry itself.
        var fileInfo = new FileInfo(path);
        try
        {
            // LinkTarget is non-null for any symlink (even dangling ones)
            if (fileInfo.LinkTarget is not null)
                throw new SecurityException($"Secret path is a symlink: {path}");
        }
        catch (SecurityException) { throw; }
        catch
        {
            // If we can't check, the path likely doesn't exist yet — which is fine
        }

        // Also verify parent directory components are not symlinks
        var dir = Path.GetDirectoryName(path);
        if (dir is not null)
        {
            var dirInfo = new DirectoryInfo(dir);
            if (dirInfo.Exists && dirInfo.LinkTarget is not null)
                throw new SecurityException($"Secret parent directory is a symlink: {dir}");
        }

        var options = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
        };

        using var stream = new FileStream(path, options);
        stream.Write(secret);
    }
}
