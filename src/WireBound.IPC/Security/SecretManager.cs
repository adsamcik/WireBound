using System.Runtime.Versioning;
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

    /// <summary>
    /// Gets the platform-appropriate path for the secret file.
    /// Windows: %LOCALAPPDATA%\WireBound\.elevation-secret
    /// Linux:   ~/.local/share/WireBound/.elevation-secret
    /// </summary>
    public static string GetSecretFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "WireBound", ".elevation-secret");
    }

    /// <summary>
    /// Generates a new cryptographically secure secret and writes it to disk
    /// with restrictive file permissions.
    /// </summary>
    public static byte[] GenerateAndStore()
    {
        var secret = RandomNumberGenerator.GetBytes(SecretLength);
        var path = GetSecretFilePath();

        var dir = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        if (File.Exists(path))
            File.Delete(path);

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
    /// </summary>
    public static byte[]? Load()
    {
        var path = GetSecretFilePath();
        if (!File.Exists(path))
            return null;

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
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static void WriteSecretWindows(string path, byte[] secret)
    {
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
        File.WriteAllBytes(path, secret);
    }

    /// <summary>
    /// Creates and writes the secret file with owner-only permissions (0600).
    /// </summary>
    [SupportedOSPlatform("linux")]
    private static void WriteSecretLinux(string path, byte[] secret)
    {
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
