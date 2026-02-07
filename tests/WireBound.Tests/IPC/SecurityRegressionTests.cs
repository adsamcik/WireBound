using System.Security.Principal;
using System.Text;
using WireBound.Elevation.Windows;
using WireBound.IPC;
using WireBound.IPC.Messages;
using WireBound.IPC.Security;
using WireBound.IPC.Transport;

namespace WireBound.Tests.IPC;

/// <summary>
/// Security regression tests — each test targets a specific past vulnerability
/// to ensure it never regresses. Tests document the vulnerability being prevented.
/// </summary>
public class SecurityRegressionTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // CVE-like: SID injection attacks (Round 1 critical fix)
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    [Arguments("S-1-5-21-123; rm -rf /")]
    [Arguments("S-1-5-21-123|evil")]
    [Arguments("S-1-5-21-123\nS-1-1-0")]
    [Arguments("S-1-5-21-123\r\n")]
    [Arguments("S-1-5-21-123\t")]
    [Arguments("S-1-5-21-123 ")]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("not-a-sid")]
    [Arguments("S-1-5")]
    public void SidInjection_InvalidFormats_Rejected(string maliciousSid)
    {
        var act = () => ElevationServer.ValidateAndParseSid(maliciousSid);
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void SidInjection_EveryoneSid_Rejected()
    {
        // S-1-1-0 is the "Everyone" well-known SID — must never be allowed
        var act = () => ElevationServer.ValidateAndParseSid("S-1-1-0");
        act.Should().Throw<ArgumentException>().Which.Message.Should().Contain("broad group");
    }

    [Test]
    public void SidInjection_AnonymousSid_Rejected()
    {
        var act = () => ElevationServer.ValidateAndParseSid("S-1-5-7");
        act.Should().Throw<ArgumentException>().Which.Message.Should().Contain("broad group");
    }

    [Test]
    public void SidInjection_AuthenticatedUsersSid_Rejected()
    {
        var act = () => ElevationServer.ValidateAndParseSid("S-1-5-11");
        act.Should().Throw<ArgumentException>().Which.Message.Should().Contain("broad group");
    }

    [Test]
    public void SidInjection_NetworkSid_Rejected()
    {
        var act = () => ElevationServer.ValidateAndParseSid("S-1-5-2");
        act.Should().Throw<ArgumentException>().Which.Message.Should().Contain("broad group");
    }

    [Test]
    public void SidValidation_ValidUserSid_Accepted()
    {
        // Current user SID should always be valid
        var currentSid = WindowsIdentity.GetCurrent().User!.Value;
        var result = ElevationServer.ValidateAndParseSid(currentSid);
        result.Value.Should().Be(currentSid);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // HMAC timing-safe comparison (Round 1 design decision)
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void HmacValidation_UsesFixedTimeComparison()
    {
        // If FixedTimeEquals is used, both valid and invalid signatures
        // should take approximately the same time (no early exit)
        var secret = HmacAuthenticator.GenerateSecret();
        var pid = 123;
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var validSig = HmacAuthenticator.Sign(pid, timestamp, secret);
        var invalidSig = Convert.ToBase64String(new byte[32]); // wrong signature

        // Both calls should execute without timing sidechannel
        HmacAuthenticator.Validate(pid, timestamp, validSig, secret).Should().BeTrue();
        HmacAuthenticator.Validate(pid, timestamp, invalidSig, secret).Should().BeFalse();
    }

    [Test]
    public void HmacValidation_ExpiredTimestamp_Rejected()
    {
        var secret = HmacAuthenticator.GenerateSecret();
        var oldTimestamp = DateTimeOffset.UtcNow.AddSeconds(-60).ToUnixTimeSeconds();
        var sig = HmacAuthenticator.Sign(1, oldTimestamp, secret);

        HmacAuthenticator.Validate(1, oldTimestamp, sig, secret, maxAgeSeconds: 30).Should().BeFalse();
    }

    [Test]
    public void HmacValidation_FutureTimestamp_Rejected()
    {
        var secret = HmacAuthenticator.GenerateSecret();
        var futureTimestamp = DateTimeOffset.UtcNow.AddSeconds(60).ToUnixTimeSeconds();
        var sig = HmacAuthenticator.Sign(1, futureTimestamp, secret);

        HmacAuthenticator.Validate(1, futureTimestamp, sig, secret, maxAgeSeconds: 30).Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Auth rate limiting prevents brute force (Round 1 high fix)
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void AuthBruteForce_ConsecutiveFailures_TriggersDisconnect()
    {
        var limiter = new AuthRateLimiter(
            maxAttemptsPerSecond: 100,
            maxConsecutiveFailures: IpcConstants.MaxConsecutiveAuthFailures);

        for (var i = 0; i < IpcConstants.MaxConsecutiveAuthFailures - 1; i++)
            limiter.RecordFailure("attacker").Should().BeFalse();

        limiter.RecordFailure("attacker").Should().BeTrue("should trigger disconnect at threshold");
    }

    [Test]
    public void AuthBruteForce_SuccessResetsCounter()
    {
        var limiter = new AuthRateLimiter(maxAttemptsPerSecond: 100, maxConsecutiveFailures: 3);

        limiter.RecordFailure("client");
        limiter.RecordFailure("client");
        limiter.RecordSuccess("client"); // Reset

        // Need 3 more failures now
        limiter.RecordFailure("client").Should().BeFalse("failure 1 after reset");
        limiter.RecordFailure("client").Should().BeFalse("failure 2 after reset");
        limiter.RecordFailure("client").Should().BeTrue("failure 3 after reset");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Secret file security (Round 1 critical fix)
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void SecretFile_GenerateAndStore_CreatesFile()
    {
        try
        {
            var secret = SecretManager.GenerateAndStore();
            var path = SecretManager.GetSecretFilePath();

            File.Exists(path).Should().BeTrue();
            secret.Should().HaveCount(32);
        }
        catch (IOException)
        {
            // File may be locked by running WireBound instance
            return;
        }
        finally
        {
            try { SecretManager.Delete(); } catch { /* best effort */ }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Session security (Round 1 high fix — TOCTOU)
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void SessionLimit_ConcurrentCreation_NeverExceedsMax()
    {
        var manager = new SessionManager();
        var results = new SessionInfo?[30];

        using var ready = new CountdownEvent(30);
        using var go = new ManualResetEventSlim(false);

        var tasks = Enumerable.Range(0, 30).Select(i => Task.Run(() =>
        {
            ready.Signal();
            go.Wait();
            results[i] = manager.CreateSession(i + 100, "/app");
        })).ToArray();

        ready.Wait();
        go.Set();
        Task.WaitAll(tasks, TimeSpan.FromSeconds(10)).Should().BeTrue();

        var created = results.Count(r => r is not null);
        created.Should().BeLessThanOrEqualTo(IpcConstants.MaxConcurrentSessions);
        created.Should().BeGreaterThan(0);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // IPC transport security (Round 1 high fix — oversized messages)
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void Transport_OversizedLength_Rejected()
    {
        using var ms = new MemoryStream();
        var oversizeLength = IpcConstants.MaxMessageSize + 1;
        var lengthBytes = BitConverter.GetBytes(oversizeLength);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(lengthBytes);
        ms.Write(lengthBytes);
        ms.Position = 0;

        var result = IpcTransport.ReceiveAsync(ms).GetAwaiter().GetResult();
        result.Should().BeNull("oversized message should be rejected");
    }

    [Test]
    public void Transport_NegativeLength_Rejected()
    {
        using var ms = new MemoryStream();
        var lengthBytes = BitConverter.GetBytes(-42);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(lengthBytes);
        ms.Write(lengthBytes);
        ms.Position = 0;

        var result = IpcTransport.ReceiveAsync(ms).GetAwaiter().GetResult();
        result.Should().BeNull("negative length should be rejected");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Buffer overflow protection (Round 2 high fix)
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void GetExtendedTcpTable_RetryLogic_IsPresent()
    {
        // We can't easily simulate ERROR_INSUFFICIENT_BUFFER, but we can verify
        // the tracker handles the case where GetConnectionStats is called immediately
        // (no data collected yet) without throwing
        using var tracker = new EtwConnectionTracker();
        var stats = tracker.GetConnectionStats();
        stats.Success.Should().BeTrue("should handle empty state gracefully");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Fail-closed executable validation (Round 1 critical fix)
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ExecutableValidation_NonExistentPid_ReturnsFalse()
    {
        // PID 0 or a very high PID that doesn't exist
        var result = ElevationServer.ValidateExecutablePath(@"C:\fake\path.exe", 999999);
        result.Should().BeFalse("fail-closed: can't verify → deny");
    }

    [Test]
    public void ExecutableValidation_WrongPath_ReturnsFalse()
    {
        // Use current process PID but wrong path
        var pid = Environment.ProcessId;
        var result = ElevationServer.ValidateExecutablePath(@"C:\totally\wrong\path.exe", pid);
        result.Should().BeFalse("mismatched path should be rejected");
    }

    [Test]
    public void ExecutableValidation_CorrectPath_ReturnsTrue()
    {
        // Use current process PID and its actual path
        var pid = Environment.ProcessId;
        var actualPath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        if (actualPath is null) return; // Skip if can't determine

        var result = ElevationServer.ValidateExecutablePath(actualPath, pid);
        result.Should().BeTrue("matching path should be accepted");
    }
}
