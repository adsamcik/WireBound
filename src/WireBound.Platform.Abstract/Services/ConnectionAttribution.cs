using WireBound.Platform.Abstract.Models;

namespace WireBound.Platform.Abstract.Services;

/// <summary>
/// Shared logic for attributing connections that the OS reports with no owning
/// process (PID 0). Common for loopback and transient sockets whose owning process
/// has already closed its handle. We recover an owner by attributing the connection
/// to the process that holds a listening TCP socket on its local port (the server
/// side) or, failing that, its remote port.
/// </summary>
public static class ConnectionAttribution
{
    /// <summary>
    /// Builds a map of TCP listening port -> owning PID from the supplied connections.
    /// </summary>
    public static Dictionary<int, int> BuildTcpListenerMap<T>(
        IEnumerable<T> connections,
        Func<T, string> protocol,
        Func<T, ConnectionState> state,
        Func<T, int> localPort,
        Func<T, int> processId)
    {
        var map = new Dictionary<int, int>();
        foreach (var c in connections)
        {
            var pid = processId(c);
            if (pid != 0 && protocol(c) == "TCP" && state(c) == ConnectionState.Listen)
                map[localPort(c)] = pid;
        }
        return map;
    }

    /// <summary>
    /// Resolves the owning PID for a connection. If the OS-reported PID is already
    /// non-zero (or the connection is not TCP) it is returned unchanged; otherwise the
    /// listening-socket owner on the local port, then the remote port, is used.
    /// Returns 0 when no owner can be recovered.
    /// </summary>
    public static int ResolveOwnerPid(
        string protocol,
        int processId,
        int localPort,
        int remotePort,
        IReadOnlyDictionary<int, int> tcpListenerPortToPid)
    {
        if (processId != 0 || protocol != "TCP")
            return processId;

        if (tcpListenerPortToPid.TryGetValue(localPort, out var localOwner))
            return localOwner;

        if (remotePort != 0 && tcpListenerPortToPid.TryGetValue(remotePort, out var remoteOwner))
            return remoteOwner;

        return processId;
    }
}
