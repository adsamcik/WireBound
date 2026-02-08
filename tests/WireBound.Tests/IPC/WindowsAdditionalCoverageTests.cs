using System.Security.Principal;
using WireBound.Elevation.Windows;
using WireBound.IPC;
using WireBound.IPC.Messages;
using WireBound.IPC.Transport;

namespace WireBound.Tests.IPC;

/// <summary>
/// Additional coverage tests for Windows elevation server components.
/// </summary>
public class WindowsAdditionalCoverageTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // ValidateAndParseSid — edge cases
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ValidateAndParseSid_SidWithMaxSubAuthorities_Succeeds()
    {
        if (!OperatingSystem.IsWindows()) return;

        // Regex allows 1–15 sub-authorities; build one with exactly 15
        var sid = "S-1-5-21-1-2-3-4-5-6-7-8-9-10-11-12-13-14";
        var result = ElevationServer.ValidateAndParseSid(sid);
        result.Value.Should().Be(sid);
    }

    [Test]
    public void ValidateAndParseSid_SidWithTooManySubAuthorities_Fails()
    {
        // 16 sub-authorities exceeds regex max of 15
        var sid = "S-1-5-21-1-2-3-4-5-6-7-8-9-10-11-12-13-14-15";
        var act = () => ElevationServer.ValidateAndParseSid(sid);
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void ValidateAndParseSid_SidWithLeadingTrailingSpaces_Fails()
    {
        var act = () => ElevationServer.ValidateAndParseSid(" S-1-5-21-123 ");
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void ValidateAndParseSid_NetworkSid_Rejected()
    {
        if (!OperatingSystem.IsWindows()) return;

        // S-1-5-2 is Network — broad group
        var act = () => ElevationServer.ValidateAndParseSid("S-1-5-2");
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void ValidateAndParseSid_AuthenticatedUserSid_Rejected()
    {
        if (!OperatingSystem.IsWindows()) return;

        // S-1-5-11 is Authenticated Users — broad group
        var act = () => ElevationServer.ValidateAndParseSid("S-1-5-11");
        act.Should().Throw<ArgumentException>();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CreateResponse
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void CreateResponse_SetsCorrectType()
    {
        var response = ElevationServer.CreateResponse(
            "req-1", MessageType.Heartbeat, new HeartbeatResponse { Alive = true });

        response.Type.Should().Be(MessageType.Heartbeat);
    }

    [Test]
    public void CreateResponse_SetsCorrectRequestId()
    {
        var response = ElevationServer.CreateResponse(
            "req-42", MessageType.Heartbeat, new HeartbeatResponse { Alive = true });

        response.RequestId.Should().Be("req-42");
    }

    [Test]
    public void CreateResponse_PayloadIsDeserializable()
    {
        var original = new HeartbeatResponse
        {
            Alive = true,
            UptimeSeconds = 3600,
            ActiveSessions = 2
        };

        var response = ElevationServer.CreateResponse("req-1", MessageType.Heartbeat, original);
        var deserialized = IpcTransport.DeserializePayload<HeartbeatResponse>(response.Payload);

        deserialized.Alive.Should().BeTrue();
        deserialized.UptimeSeconds.Should().Be(3600);
        deserialized.ActiveSessions.Should().Be(2);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CreateErrorResponse
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void CreateErrorResponse_SetsErrorType()
    {
        var response = ElevationServer.CreateErrorResponse("req-err", "something failed");

        response.Type.Should().Be(MessageType.Error);
    }

    [Test]
    public void CreateErrorResponse_PayloadContainsErrorMessage()
    {
        var response = ElevationServer.CreateErrorResponse("req-err", "something failed");
        var error = IpcTransport.DeserializePayload<ErrorResponse>(response.Payload);

        error.Error.Should().Be("something failed");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ValidateExecutablePath
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ValidateExecutablePath_CurrentProcess_Matches()
    {
        if (!OperatingSystem.IsWindows()) return;

        var pid = Environment.ProcessId;
        var path = Environment.ProcessPath!;

        ElevationServer.ValidateExecutablePath(path, pid).Should().BeTrue();
    }

    [Test]
    public void ValidateExecutablePath_CaseDifferentPath_StillMatches()
    {
        if (!OperatingSystem.IsWindows()) return;

        var pid = Environment.ProcessId;
        var path = Environment.ProcessPath!.ToUpperInvariant();

        ElevationServer.ValidateExecutablePath(path, pid).Should().BeTrue();
    }

    [Test]
    public void ValidateExecutablePath_NonexistentPid_ReturnsFalse()
    {
        if (!OperatingSystem.IsWindows()) return;

        ElevationServer.ValidateExecutablePath(@"C:\fake.exe", 999999).Should().BeFalse();
    }

    [Test]
    public void ValidateExecutablePath_EmptyPath_ReturnsFalse()
    {
        if (!OperatingSystem.IsWindows()) return;

        var pid = Environment.ProcessId;
        ElevationServer.ValidateExecutablePath("", pid).Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MakeConnectionKey — IPv6 boundary
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void MakeConnectionKey_WithIpv6Addresses_FormatsCorrectly()
    {
        if (!OperatingSystem.IsWindows()) return;

        var key = EtwConnectionTracker.MakeConnectionKey(
            "2001:db8::1", 443, "fe80::1%eth0", 8080);

        key.Should().Be("2001:db8::1:443-fe80::1%eth0:8080");
    }
}
