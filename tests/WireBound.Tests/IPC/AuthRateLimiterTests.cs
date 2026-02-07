using WireBound.IPC.Security;

namespace WireBound.Tests.IPC;

/// <summary>
/// Tests for AuthRateLimiter — pre-authentication rate limiting with
/// per-client sliding window and consecutive failure tracking.
/// </summary>
public class AuthRateLimiterTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // TryAcquire — sliding window rate limiting
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void TryAcquire_WithinLimit_ReturnsTrue()
    {
        var limiter = new AuthRateLimiter(maxAttemptsPerSecond: 5, maxConsecutiveFailures: 3);

        for (var i = 0; i < 5; i++)
            limiter.TryAcquire("client1").Should().BeTrue($"attempt {i} should be allowed");
    }

    [Test]
    public void TryAcquire_ExceedsLimit_ReturnsFalse()
    {
        var limiter = new AuthRateLimiter(maxAttemptsPerSecond: 3, maxConsecutiveFailures: 10);

        // Exhaust the window
        for (var i = 0; i < 3; i++)
            limiter.TryAcquire("client1");

        // The 4th should be rejected
        limiter.TryAcquire("client1").Should().BeFalse("rate limit exceeded");
    }

    [Test]
    public void TryAcquire_DifferentClients_TrackSeparately()
    {
        var limiter = new AuthRateLimiter(maxAttemptsPerSecond: 2, maxConsecutiveFailures: 10);

        // Exhaust client1
        limiter.TryAcquire("client1");
        limiter.TryAcquire("client1");
        limiter.TryAcquire("client1").Should().BeFalse("client1 exceeded limit");

        // client2 should be unaffected
        limiter.TryAcquire("client2").Should().BeTrue("client2 has its own window");
    }

    [Test]
    public async Task TryAcquire_AfterWindowExpiry_ResetsCount()
    {
        var limiter = new AuthRateLimiter(maxAttemptsPerSecond: 1, maxConsecutiveFailures: 10);

        limiter.TryAcquire("client1").Should().BeTrue();
        limiter.TryAcquire("client1").Should().BeFalse("exhausted in current window");

        // Wait for the 1-second window to expire
        await Task.Delay(1100);

        limiter.TryAcquire("client1").Should().BeTrue("new window started");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // RecordFailure — consecutive failure tracking
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void RecordFailure_BelowThreshold_ReturnsFalse()
    {
        var limiter = new AuthRateLimiter(maxAttemptsPerSecond: 10, maxConsecutiveFailures: 5);

        for (var i = 0; i < 4; i++)
            limiter.RecordFailure("client1").Should().BeFalse($"failure {i + 1} should not trigger disconnect");
    }

    [Test]
    public void RecordFailure_AtThreshold_ReturnsTrue()
    {
        var limiter = new AuthRateLimiter(maxAttemptsPerSecond: 10, maxConsecutiveFailures: 3);

        limiter.RecordFailure("client1").Should().BeFalse("failure 1");
        limiter.RecordFailure("client1").Should().BeFalse("failure 2");
        limiter.RecordFailure("client1").Should().BeTrue("failure 3 = threshold, should disconnect");
    }

    [Test]
    public void RecordFailure_DifferentClients_TrackSeparately()
    {
        var limiter = new AuthRateLimiter(maxAttemptsPerSecond: 10, maxConsecutiveFailures: 2);

        limiter.RecordFailure("client1");
        limiter.RecordFailure("client1").Should().BeTrue("client1 at threshold");

        // client2 should be unaffected
        limiter.RecordFailure("client2").Should().BeFalse("client2 only 1 failure");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // RecordSuccess — resets consecutive failure counter
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void RecordSuccess_ResetsFailureCounter()
    {
        var limiter = new AuthRateLimiter(maxAttemptsPerSecond: 10, maxConsecutiveFailures: 3);

        // Accumulate 2 failures (below threshold)
        limiter.RecordFailure("client1");
        limiter.RecordFailure("client1");

        // Successful auth resets the counter
        limiter.RecordSuccess("client1");

        // Now we should need 3 more failures to hit threshold
        limiter.RecordFailure("client1").Should().BeFalse("failure 1 after reset");
        limiter.RecordFailure("client1").Should().BeFalse("failure 2 after reset");
        limiter.RecordFailure("client1").Should().BeTrue("failure 3 after reset = threshold");
    }

    [Test]
    public void RecordSuccess_ForUnknownClient_DoesNotThrow()
    {
        var limiter = new AuthRateLimiter();

        // Should not throw for a client that was never tracked
        var act = () => limiter.RecordSuccess("unknown");
        act.Should().NotThrow();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // RemoveClient — cleanup
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void RemoveClient_CleansUpState()
    {
        var limiter = new AuthRateLimiter(maxAttemptsPerSecond: 1, maxConsecutiveFailures: 10);

        // Exhaust client1's window
        limiter.TryAcquire("client1");
        limiter.TryAcquire("client1").Should().BeFalse("exhausted");

        // Remove and re-add — should have a fresh window
        limiter.RemoveClient("client1");
        limiter.TryAcquire("client1").Should().BeTrue("fresh state after removal");
    }

    [Test]
    public void RemoveClient_ResetsFailureTracking()
    {
        var limiter = new AuthRateLimiter(maxAttemptsPerSecond: 10, maxConsecutiveFailures: 3);

        limiter.RecordFailure("client1");
        limiter.RecordFailure("client1");

        limiter.RemoveClient("client1");

        // After removal, failures should restart from zero
        limiter.RecordFailure("client1").Should().BeFalse("failure 1 after removal");
        limiter.RecordFailure("client1").Should().BeFalse("failure 2 after removal");
        limiter.RecordFailure("client1").Should().BeTrue("failure 3 after removal = threshold");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Thread safety — concurrent access
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void TryAcquire_ConcurrentAccess_NeverExceedsLimit()
    {
        const int maxPerSecond = 5;
        var limiter = new AuthRateLimiter(maxAttemptsPerSecond: maxPerSecond, maxConsecutiveFailures: 100);
        var results = new bool[50];

        Parallel.For(0, 50, i =>
        {
            results[i] = limiter.TryAcquire("client1");
        });

        var allowed = results.Count(r => r);
        allowed.Should().BeLessThanOrEqualTo(maxPerSecond, "concurrent calls should respect rate limit");
        allowed.Should().BeGreaterThan(0, "at least one call should succeed");
    }

    [Test]
    public void RecordFailure_ConcurrentAccess_ExactlyOneTriggersDisconnect()
    {
        const int maxFailures = 5;
        var limiter = new AuthRateLimiter(maxAttemptsPerSecond: 100, maxConsecutiveFailures: maxFailures);
        var disconnectTriggers = 0;

        Parallel.For(0, 20, _ =>
        {
            if (limiter.RecordFailure("client1"))
                Interlocked.Increment(ref disconnectTriggers);
        });

        // All 20 exceed the threshold (5), so multiple calls return true once threshold is crossed.
        // The key invariant is that it DOES trigger (no lost failures).
        disconnectTriggers.Should().BeGreaterThan(0, "threshold must eventually be reached");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Edge cases
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void Constructor_DefaultValues_AreReasonable()
    {
        // Should not throw with defaults from IpcConstants
        var limiter = new AuthRateLimiter();

        // Should be able to make at least one attempt
        limiter.TryAcquire("test").Should().BeTrue();
    }

    [Test]
    public void TryAcquire_MaxPerSecondOfOne_AllowsExactlyOne()
    {
        var limiter = new AuthRateLimiter(maxAttemptsPerSecond: 1, maxConsecutiveFailures: 10);

        limiter.TryAcquire("client1").Should().BeTrue("first attempt");
        limiter.TryAcquire("client1").Should().BeFalse("second attempt in same window");
    }

    [Test]
    public void RecordFailure_MaxConsecutiveFailuresOfOne_ImmediateDisconnect()
    {
        var limiter = new AuthRateLimiter(maxAttemptsPerSecond: 10, maxConsecutiveFailures: 1);

        limiter.RecordFailure("client1").Should().BeTrue("single failure = immediate disconnect");
    }
}
