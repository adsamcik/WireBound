using System.Security.Cryptography;
using System.Text;

namespace WireBound.IPC.Security;

/// <summary>
/// HMAC-SHA256 authentication for IPC messages.
/// </summary>
public static class HmacAuthenticator
{
    /// <summary>
    /// Generates a shared secret for a session.
    /// </summary>
    public static byte[] GenerateSecret()
    {
        return RandomNumberGenerator.GetBytes(32);
    }

    /// <summary>
    /// Creates an HMAC signature for authentication.
    /// </summary>
    public static string Sign(int pid, long timestamp, byte[] secret)
    {
        var data = $"{pid}:{timestamp}";
        using var hmac = new HMACSHA256(secret);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Validates an HMAC signature.
    /// </summary>
    public static bool Validate(int pid, long timestamp, string signature, byte[] secret, int maxAgeSeconds = 30)
    {
        // Check timestamp freshness
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(now - timestamp) > maxAgeSeconds) return false;

        var expected = Sign(pid, timestamp, secret);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signature));
    }
}
