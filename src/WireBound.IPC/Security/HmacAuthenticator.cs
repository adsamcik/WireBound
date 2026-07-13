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
    /// Creates an HMAC signature for authentication (legacy timestamp-only form).
    /// Retained for backward-compatible mutual-auth in <see cref="SignServer"/>.
    /// </summary>
    public static string Sign(int pid, long timestamp, byte[] secret)
    {
        var data = $"{pid}:{timestamp}";
        using var hmac = new HMACSHA256(secret);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Creates an HMAC signature binding the client's PID, image hash and the
    /// server-issued nonce together. Used by the modern challenge-response
    /// authentication flow. The image hash binds the signature to the actual
    /// binary on disk; the nonce binds it to this specific connection.
    /// </summary>
    public static string SignWithNonce(int pid, ReadOnlySpan<byte> imageHash, ReadOnlySpan<byte> nonce, byte[] secret)
    {
        using var hmac = new HMACSHA256(secret);
        var pidBytes = BitConverter.GetBytes(pid);
        hmac.TransformBlock(pidBytes, 0, pidBytes.Length, null, 0);
        var separator = new byte[] { 0xFF };
        hmac.TransformBlock(separator, 0, 1, null, 0);
        var imgArr = imageHash.ToArray();
        hmac.TransformBlock(imgArr, 0, imgArr.Length, null, 0);
        hmac.TransformBlock(separator, 0, 1, null, 0);
        var nonceArr = nonce.ToArray();
        hmac.TransformFinalBlock(nonceArr, 0, nonceArr.Length);
        return Convert.ToBase64String(hmac.Hash!);
    }

    /// <summary>
    /// Server-side mutual-auth signature over the issued session id and the
    /// client's nonce — proves to the client that the server holds the secret
    /// AND that the response is bound to this specific connection.
    /// </summary>
    public static string SignServerResponse(string sessionId, long expiresAtUtc, ReadOnlySpan<byte> nonce, byte[] secret)
    {
        var prefix = Encoding.UTF8.GetBytes($"{sessionId}:{expiresAtUtc}:");
        using var hmac = new HMACSHA256(secret);
        hmac.TransformBlock(prefix, 0, prefix.Length, null, 0);
        var nonceArr = nonce.ToArray();
        hmac.TransformFinalBlock(nonceArr, 0, nonceArr.Length);
        return Convert.ToBase64String(hmac.Hash!);
    }

    /// <summary>
    /// Validates a nonce-bound HMAC signature in constant time.
    /// </summary>
    public static bool ValidateWithNonce(int pid, ReadOnlySpan<byte> imageHash, ReadOnlySpan<byte> nonce, string signature, byte[] secret)
    {
        var expected = SignWithNonce(pid, imageHash, nonce, secret);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signature));
    }

    /// <summary>
    /// Validates a legacy timestamp-only HMAC signature. Retained for the
    /// fallback mutual-auth path and pre-challenge clients during rollout.
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
