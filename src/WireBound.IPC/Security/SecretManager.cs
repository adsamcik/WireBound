using System.Security.Cryptography;
using System.Text;

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

        File.WriteAllBytes(path, secret);
        RestrictFileAccess(path);

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
    /// Sets restrictive file permissions so only the current user (and root/admin) can read.
    /// </summary>
    private static void RestrictFileAccess(string path)
    {
        if (OperatingSystem.IsLinux())
        {
            // chmod 600 — owner read/write only
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        // Windows: The file inherits %LOCALAPPDATA% ACLs which are user-private by default.
        // No additional ACL manipulation needed.
    }
}
