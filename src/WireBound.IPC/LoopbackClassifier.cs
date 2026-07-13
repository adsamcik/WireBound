using System.Net;

namespace WireBound.IPC;

/// <summary>
/// Classifies remote addresses as loopback/localhost (127.0.0.0/8, ::1) so that
/// per-app traffic can be split into "local" vs real "network" buckets.
/// Shared by the elevated helpers (Windows ETW, Linux netlink) which own the
/// per-connection remote-address data.
/// </summary>
public static class LoopbackClassifier
{
    /// <summary>
    /// Returns true when <paramref name="remoteAddress"/> parses to a loopback
    /// IP address. Unparseable or empty addresses are treated as non-loopback
    /// (i.e. counted as network) so unknown traffic is never hidden.
    /// </summary>
    public static bool IsLoopback(string? remoteAddress)
    {
        if (string.IsNullOrEmpty(remoteAddress))
            return false;

        return IPAddress.TryParse(remoteAddress, out var ip) && IPAddress.IsLoopback(ip);
    }
}
