using System.Security.Principal;
using WireBound.Elevation.Windows;

namespace WireBound.Tests.IPC;

public class SidValidationTests
{
    [Test]
    public void ValidateAndParseSid_ValidUserSid_Succeeds()
    {
        if (!OperatingSystem.IsWindows()) return;

        // A typical user SID
        var sid = WindowsIdentity.GetCurrent().User!.Value;
        var result = ElevationServer.ValidateAndParseSid(sid);
        result.Value.Should().Be(sid);
    }

    [Test]
    public void ValidateAndParseSid_EmptyString_Throws()
    {
        var act = () => ElevationServer.ValidateAndParseSid("");
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void ValidateAndParseSid_NullString_Throws()
    {
        var act = () => ElevationServer.ValidateAndParseSid(null!);
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void ValidateAndParseSid_GarbageString_Throws()
    {
        var act = () => ElevationServer.ValidateAndParseSid("not-a-sid");
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void ValidateAndParseSid_InjectionAttempt_Throws()
    {
        // Attempt to inject a pipe name or path via SID argument
        var act = () => ElevationServer.ValidateAndParseSid("S-1-5-21-1234; rm -rf /");
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void ValidateAndParseSid_WorldSid_Throws()
    {
        if (!OperatingSystem.IsWindows()) return;

        // S-1-1-0 is "Everyone" — must be rejected
        var act = () => ElevationServer.ValidateAndParseSid("S-1-1-0");
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void ValidateAndParseSid_AnonymousSid_Throws()
    {
        if (!OperatingSystem.IsWindows()) return;

        // S-1-5-7 is "Anonymous" — must be rejected
        var act = () => ElevationServer.ValidateAndParseSid("S-1-5-7");
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void ValidateAndParseSid_AuthenticatedUsersSid_Throws()
    {
        if (!OperatingSystem.IsWindows()) return;

        // S-1-5-11 is "Authenticated Users" — must be rejected
        var act = () => ElevationServer.ValidateAndParseSid("S-1-5-11");
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void ValidateAndParseSid_NetworkSid_Throws()
    {
        if (!OperatingSystem.IsWindows()) return;

        // S-1-5-2 is "Network" — must be rejected
        var act = () => ElevationServer.ValidateAndParseSid("S-1-5-2");
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void ValidateAndParseSid_LocalSystemSid_Succeeds()
    {
        if (!OperatingSystem.IsWindows()) return;

        // SYSTEM (S-1-5-18) should be allowed — it's a specific identity
        var result = ElevationServer.ValidateAndParseSid("S-1-5-18");
        result.Value.Should().Be("S-1-5-18");
    }
}
