using WireBound.IPC.Security;

namespace WireBound.Tests.IPC;

/// <summary>
/// Test-only <see cref="IClientIdentityVerifier"/> that delegates to a
/// configurable function. Lets tests focus on HMAC / nonce / single-client
/// bind logic without dragging in real OS-level process inspection
/// (which is exercised by separate verifier-specific tests).
///
/// <para>
/// The default behaviour approves every caller and returns
/// <see cref="Environment.ProcessPath"/> as the "verified" image path —
/// matching the path the tests sign with via
/// <see cref="ClientImageHasher.HashFile"/>. Pass a custom delegate to
/// simulate rejection or a different verified path.
/// </para>
/// </summary>
internal sealed class PassThroughClientIdentityVerifier : IClientIdentityVerifier
{
    private readonly Func<int, byte[], ClientIdentityResult> _verify;

    public PassThroughClientIdentityVerifier()
        : this(static (_, _) => ClientIdentityResult.Valid(
            Environment.ProcessPath ?? throw new InvalidOperationException(
                "Environment.ProcessPath is unavailable; pass an explicit verifier delegate.")))
    {
    }

    /// <summary>
    /// Constructs a verifier whose decision is computed by the given delegate.
    /// The delegate receives the kernel-verified <paramref name="pid"/> and a
    /// COPY of the claimed SHA-256 image hash (the production interface uses a
    /// <see cref="ReadOnlySpan{T}"/> which cannot be captured in a delegate;
    /// the copy is safe to retain).
    /// </summary>
    public PassThroughClientIdentityVerifier(Func<int, byte[], ClientIdentityResult> verify)
    {
        ArgumentNullException.ThrowIfNull(verify);
        _verify = verify;
    }

    public ClientIdentityResult Verify(int pid, ReadOnlySpan<byte> claimedImageHash)
        => _verify(pid, claimedImageHash.ToArray());
}
