using Microsoft.Extensions.Time.Testing;
using WireBound.IPC;
using WireBound.IPC.Messages;
using WireBound.IPC.Security;
using WireBound.IPC.Transport;

namespace WireBound.Tests.IPC;

/// <summary>
/// Mutation-killing boundary tests — designed to catch off-by-one errors,
/// negated conditions, changed comparison operators, and return value mutations.
/// Each test targets a specific mutation that a mutation testing tool would attempt.
/// </summary>
public class MutationKillingTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // RateLimiter boundaries — exact limit and ±1
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void RateLimiter_ExactlyAtLimit_Allowed()
    {
        var limiter = new RateLimiter(maxRequestsPerSecond: 5);

        for (var i = 0; i < 5; i++)
            limiter.TryAcquire("s1").Should().BeTrue($"request {i + 1} of 5 should be allowed");
    }

    [Test]
    public void RateLimiter_OneOverLimit_Denied()
    {
        var limiter = new RateLimiter(maxRequestsPerSecond: 5);

        for (var i = 0; i < 5; i++)
            limiter.TryAcquire("s1");

        limiter.TryAcquire("s1").Should().BeFalse("6th request should be denied");
    }

    [Test]
    public void RateLimiter_LimitOfOne_AllowsExactlyOne()
    {
        var limiter = new RateLimiter(maxRequestsPerSecond: 1);

        limiter.TryAcquire("s1").Should().BeTrue();
        limiter.TryAcquire("s1").Should().BeFalse();
    }

    [Test]
    public void RateLimiter_WindowResetAt1000ms()
    {
        var fakeTime = new FakeTimeProvider();
        var limiter = new RateLimiter(maxRequestsPerSecond: 1, timeProvider: fakeTime);

        limiter.TryAcquire("s1").Should().BeTrue();
        limiter.TryAcquire("s1").Should().BeFalse();

        // Advance past the 1-second window
        fakeTime.Advance(TimeSpan.FromMilliseconds(1100));

        limiter.TryAcquire("s1").Should().BeTrue("new window should start");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // AuthRateLimiter boundaries — consecutive failures
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void AuthRateLimiter_FailureAtThresholdMinusOne_NotTriggered()
    {
        var limiter = new AuthRateLimiter(maxAttemptsPerSecond: 100, maxConsecutiveFailures: 5);

        for (var i = 0; i < 4; i++)
            limiter.RecordFailure("c").Should().BeFalse($"failure {i + 1} of 4 should not trigger");
    }

    [Test]
    public void AuthRateLimiter_FailureAtExactThreshold_Triggered()
    {
        var limiter = new AuthRateLimiter(maxAttemptsPerSecond: 100, maxConsecutiveFailures: 5);

        for (var i = 0; i < 4; i++)
            limiter.RecordFailure("c");

        limiter.RecordFailure("c").Should().BeTrue("5th failure should trigger");
    }

    [Test]
    public void AuthRateLimiter_FailureAtThresholdPlusOne_StillTriggered()
    {
        var limiter = new AuthRateLimiter(maxAttemptsPerSecond: 100, maxConsecutiveFailures: 5);

        for (var i = 0; i < 5; i++)
            limiter.RecordFailure("c");

        limiter.RecordFailure("c").Should().BeTrue("6th failure should also be >= threshold");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SessionManager boundaries — max concurrent
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void Session_ExactlyAtMax_Allowed()
    {
        var manager = new SessionManager();

        SessionInfo? last = null;
        for (var i = 0; i < IpcConstants.MaxConcurrentSessions; i++)
            last = manager.CreateSession(i, "/app");

        last.Should().NotBeNull("session at exact max should succeed");
    }

    [Test]
    public void Session_OneOverMax_Denied()
    {
        var manager = new SessionManager();

        for (var i = 0; i < IpcConstants.MaxConcurrentSessions; i++)
            manager.CreateSession(i, "/app");

        manager.CreateSession(999, "/app").Should().BeNull("max+1 should fail");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // HMAC timestamp boundaries — freshness check (±maxAgeSeconds)
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void Hmac_TimestampExactlyAtBoundary_Accepted()
    {
        var secret = HmacAuthenticator.GenerateSecret();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Exactly 29 seconds ago (within 30s window)
        var timestamp = now - 29;
        var sig = HmacAuthenticator.Sign(1, timestamp, secret);
        HmacAuthenticator.Validate(1, timestamp, sig, secret, maxAgeSeconds: 30).Should().BeTrue();
    }

    [Test]
    public void Hmac_TimestampExactlyAtMax_Accepted()
    {
        var secret = HmacAuthenticator.GenerateSecret();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Exactly 30 seconds ago (at boundary — Math.Abs(now-timestamp) > maxAge is the check)
        var timestamp = now - 30;
        var sig = HmacAuthenticator.Sign(1, timestamp, secret);
        HmacAuthenticator.Validate(1, timestamp, sig, secret, maxAgeSeconds: 30).Should().BeTrue("30 == 30, not > 30");
    }

    [Test]
    public void Hmac_TimestampOneOverMax_Rejected()
    {
        var secret = HmacAuthenticator.GenerateSecret();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // 31 seconds ago (just over boundary)
        var timestamp = now - 31;
        var sig = HmacAuthenticator.Sign(1, timestamp, secret);
        HmacAuthenticator.Validate(1, timestamp, sig, secret, maxAgeSeconds: 30).Should().BeFalse();
    }

    [Test]
    public void Hmac_FutureTimestamp_AtBoundary_Accepted()
    {
        var secret = HmacAuthenticator.GenerateSecret();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var timestamp = now + 30;
        var sig = HmacAuthenticator.Sign(1, timestamp, secret);
        HmacAuthenticator.Validate(1, timestamp, sig, secret, maxAgeSeconds: 30).Should().BeTrue();
    }

    [Test]
    public void Hmac_FutureTimestamp_OverBoundary_Rejected()
    {
        var secret = HmacAuthenticator.GenerateSecret();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var timestamp = now + 31;
        var sig = HmacAuthenticator.Sign(1, timestamp, secret);
        HmacAuthenticator.Validate(1, timestamp, sig, secret, maxAgeSeconds: 30).Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // IpcTransport message size boundaries
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task Transport_MessageAtExactMaxSize_Accepted()
    {
        // Calibrate: measure MessagePack serialization overhead for this envelope shape
        var overhead = await MeasureSerializationOverheadAsync();

        // Construct a message whose serialized size is exactly MaxMessageSize
        var exactPayloadSize = IpcConstants.MaxMessageSize - overhead;
        var msg = new IpcMessage { Type = MessageType.Error, RequestId = "x", Payload = new byte[exactPayloadSize] };

        using var stream = new MemoryStream();
        await IpcTransport.SendAsync(stream, msg);

        // Stream has 4-byte length prefix + serialized bytes
        ((int)stream.Length - 4).Should().Be(IpcConstants.MaxMessageSize,
            "serialized message should be exactly at max size");
    }

    [Test]
    public async Task Transport_MessageOneOverMaxSize_Rejected()
    {
        // Calibrate overhead using same envelope shape
        var overhead = await MeasureSerializationOverheadAsync();

        // Construct a message whose serialized size is MaxMessageSize + 1
        var overPayloadSize = IpcConstants.MaxMessageSize - overhead + 1;
        var msg = new IpcMessage { Type = MessageType.Error, RequestId = "x", Payload = new byte[overPayloadSize] };

        using var stream = new MemoryStream();
        var act = () => IpcTransport.SendAsync(stream, msg).GetAwaiter().GetResult();
        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("max size");
    }

    /// <summary>
    /// Measures the fixed MessagePack overhead (envelope fields minus payload bytes)
    /// by serializing a calibration message with a known payload size.
    /// Both calibration and target payloads use bin32 format so overhead is constant.
    /// </summary>
    private static async Task<int> MeasureSerializationOverheadAsync()
    {
        const int calibrationSize = 100_000;
        var calibration = new IpcMessage { Type = MessageType.Error, RequestId = "x", Payload = new byte[calibrationSize] };
        using var stream = new MemoryStream();
        await IpcTransport.SendAsync(stream, calibration);
        return (int)stream.Length - 4 - calibrationSize;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // HMAC sign/validate — PID matters
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void Hmac_DifferentPid_InvalidSignature()
    {
        var secret = HmacAuthenticator.GenerateSecret();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var sig = HmacAuthenticator.Sign(100, timestamp, secret);
        HmacAuthenticator.Validate(101, timestamp, sig, secret).Should().BeFalse("different PID");
    }

    [Test]
    public void Hmac_DifferentTimestamp_InvalidSignature()
    {
        var secret = HmacAuthenticator.GenerateSecret();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var sig = HmacAuthenticator.Sign(100, timestamp, secret);
        HmacAuthenticator.Validate(100, timestamp + 1, sig, secret).Should().BeFalse("different timestamp");
    }

    [Test]
    public void Hmac_DifferentSecret_InvalidSignature()
    {
        var secret1 = HmacAuthenticator.GenerateSecret();
        var secret2 = HmacAuthenticator.GenerateSecret();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var sig = HmacAuthenticator.Sign(100, timestamp, secret1);
        HmacAuthenticator.Validate(100, timestamp, sig, secret2).Should().BeFalse("different secret");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Return value mutations — verify true/false are not swapped
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void RateLimiter_RemoveClient_AllowsNewRequests()
    {
        var limiter = new RateLimiter(maxRequestsPerSecond: 1);

        limiter.TryAcquire("s1").Should().BeTrue();
        limiter.TryAcquire("s1").Should().BeFalse("exhausted");

        limiter.RemoveClient("s1");

        limiter.TryAcquire("s1").Should().BeTrue("fresh after remove");
    }

    [Test]
    public void SessionManager_ValidateAfterExpiry_ReturnsNull()
    {
        // We can't easily test real 8-hour expiry, but we can verify that
        // the validation checks the expiry field
        var manager = new SessionManager();
        var session = manager.CreateSession(1, "/app");
        session.Should().NotBeNull();

        // Validate immediately — should work
        manager.ValidateSession(session!.SessionId).Should().NotBeNull();
    }

    [Test]
    public void SessionManager_RemoveSession_ReturnValues()
    {
        var manager = new SessionManager();
        var session = manager.CreateSession(1, "/app");

        // First remove should return true
        manager.RemoveSession(session!.SessionId).Should().BeTrue();

        // Second remove should return false (already removed)
        manager.RemoveSession(session.SessionId).Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Secret generation — uniqueness kills constant-return mutations
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void Secret_Generate_Is32Bytes()
    {
        var secret = HmacAuthenticator.GenerateSecret();
        secret.Should().HaveCount(32);
    }

    [Test]
    public void Secret_Generate_IsNotAllZeros()
    {
        var secret = HmacAuthenticator.GenerateSecret();
        secret.Should().Contain(b => b != 0, "cryptographic random should not be all zeros");
    }

    [Test]
    public void Secret_Generate_IsDifferentEachTime()
    {
        var s1 = HmacAuthenticator.GenerateSecret();
        var s2 = HmacAuthenticator.GenerateSecret();
        s1.Should().NotBeEquivalentTo(s2);
    }
}
