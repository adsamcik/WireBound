namespace WireBound.IPC.Security;

/// <summary>
/// Verifies the identity of a process connecting to the elevated helper.
///
/// <para>
/// The "is this caller really the legitimate WireBound main app?" decision is
/// the single load-bearing security check for the elevated helper. The HMAC
/// secret on disk is readable by any same-user process and therefore cannot
/// by itself authenticate the caller. The verifier defeats the realistic
/// "same-user attacker reads the secret" attack by binding authentication
/// to OS-verified identity:
/// <list type="bullet">
///   <item>Windows: <c>QueryFullProcessImageName</c> + Authenticode signature with
///         pinned signer thumbprint.</item>
///   <item>Linux: <c>readlink /proc/&lt;pid&gt;/exe</c> + inode/device equality
///         against the install path. Kernel-verified, race-free.</item>
/// </list>
/// </para>
/// </summary>
public interface IClientIdentityVerifier
{
    /// <summary>
    /// Verifies that the process with <paramref name="pid"/> really is running
    /// from the legitimate, signed/installed WireBound main app binary.
    /// </summary>
    /// <param name="pid">
    /// The kernel-verified PID of the connecting client (must come from
    /// <c>GetNamedPipeClientProcessId</c> or <c>SO_PEERCRED</c>, NOT from
    /// the auth message payload).
    /// </param>
    /// <param name="claimedImageHash">
    /// The SHA-256 of the client's own executable as claimed by the client.
    /// The verifier independently recomputes this from the disk file at the
    /// OS-reported image path and compares with constant-time equality.
    /// </param>
    /// <returns>Outcome of the verification, including a reason string.</returns>
    ClientIdentityResult Verify(int pid, ReadOnlySpan<byte> claimedImageHash);
}

/// <summary>
/// Outcome of an <see cref="IClientIdentityVerifier.Verify"/> call.
/// </summary>
public readonly record struct ClientIdentityResult(
    bool IsValid,
    string? VerifiedImagePath,
    string? Reason)
{
    public static ClientIdentityResult Valid(string path)
        => new(true, path, null);

    public static ClientIdentityResult Invalid(string reason)
        => new(false, null, reason);
}
