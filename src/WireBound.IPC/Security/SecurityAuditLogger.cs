using Microsoft.Extensions.Logging;

namespace WireBound.IPC.Security;

/// <summary>
/// Structured audit log for elevation-helper authentication events.
///
/// <para>
/// Writes to an <see cref="ILogger"/> tagged with an <c>AuditChannel</c>
/// scope so security-relevant events can be routed to a separate sink with
/// append-only permissions and a longer retention policy than the noisy
/// app log. The Elevation host wires this to its Serilog logger; tests can
/// supply a NullLogger or a recording fake.
/// </para>
///
/// <para>
/// These events are forensic (not preventive): they cannot stop an attack
/// in progress but make a successful in-process injection visible to anyone
/// reviewing the helper's audit trail post-hoc.
/// </para>
/// </summary>
public sealed class SecurityAuditLogger
{
    private readonly ILogger _logger;

    public SecurityAuditLogger(ILogger? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    }

    /// <summary>
    /// Logs a successful authentication. Includes the verified identity.
    /// </summary>
    public void AuthSuccess(int peerPid, string verifiedPath, string sessionId, string? signerThumbprint = null)
    {
        _logger.LogInformation(
            "AUDIT auth-success peer_pid={PeerPid} verified_path={VerifiedPath} session_id={SessionId} signer={Signer}",
            peerPid, verifiedPath, sessionId, signerThumbprint ?? "<none>");
    }

    /// <summary>
    /// Logs a failed authentication. Includes the reason so post-hoc review
    /// can distinguish "wrong binary," "missing nonce," "stale secret," etc.
    /// </summary>
    public void AuthFailure(int peerPid, string? claimedPath, string reason)
    {
        _logger.LogWarning(
            "AUDIT auth-failure peer_pid={PeerPid} claimed_path={ClaimedPath} reason={Reason}",
            peerPid, claimedPath ?? "<none>", reason);
    }

    /// <summary>
    /// Logs that the helper refused a connection from an unexpected user/UID.
    /// </summary>
    public void RejectedPeer(int peerPid, int actualId, int expectedId, string idKind)
    {
        _logger.LogWarning(
            "AUDIT rejected-peer peer_pid={PeerPid} actual_{IdKind}={Actual} expected_{IdKind}={Expected}",
            peerPid, idKind, actualId, idKind, expectedId);
    }

    /// <summary>
    /// Logs that a second client tried to bind to a helper instance that is
    /// already locked to a different identity (single-client-per-helper).
    /// </summary>
    public void SecondClientRejected(int peerPid, string verifiedPath, string boundPath)
    {
        _logger.LogWarning(
            "AUDIT second-client-rejected peer_pid={PeerPid} verified_path={VerifiedPath} bound_to={BoundPath}",
            peerPid, verifiedPath, boundPath);
    }

    /// <summary>
    /// Logs that the helper is bootstrapping. One per process lifetime.
    /// </summary>
    public void HelperStarted(int helperPid, string secretPath)
    {
        _logger.LogInformation(
            "AUDIT helper-started helper_pid={HelperPid} secret_path={SecretPath}",
            helperPid, secretPath);
    }
}
