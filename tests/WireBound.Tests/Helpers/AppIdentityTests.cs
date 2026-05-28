using WireBound.Platform.Abstract.Helpers;

namespace WireBound.Tests.Helpers;

/// <summary>
/// Regression coverage for <see cref="AppIdentity"/>.
/// </summary>
/// <remarks>
/// Pins the contract that downstream code (<c>DataPersistenceService.SaveAppStatsAsync</c>,
/// <c>WindowsElevatedProcessNetworkProvider.EnrichAndComputeSpeeds</c>) relies on:
/// the same executable path always hashes to the same identifier regardless
/// of casing, and an empty/missing path falls back to the well-known
/// <see cref="AppIdentity.UnknownIdentifier"/> sentinel rather than producing
/// an empty string (which would cause silent record drops in persistence).
/// </remarks>
public class AppIdentityTests
{
    [Test]
    public void ComputeAppIdentifier_SamePath_IsDeterministic()
    {
        var first = AppIdentity.ComputeAppIdentifier("C:\\Program Files\\App\\app.exe");
        var second = AppIdentity.ComputeAppIdentifier("C:\\Program Files\\App\\app.exe");

        first.Should().Be(second);
        first.Should().HaveLength(16);
    }

    [Test]
    public void ComputeAppIdentifier_DifferentCasing_IsTreatedAsSameApp()
    {
        var lower = AppIdentity.ComputeAppIdentifier("c:\\program files\\app\\app.exe");
        var upper = AppIdentity.ComputeAppIdentifier("C:\\PROGRAM FILES\\APP\\APP.EXE");

        upper.Should().Be(lower);
    }

    [Test]
    public void ComputeAppIdentifier_DifferentPaths_ProduceDifferentIdentifiers()
    {
        var firefox = AppIdentity.ComputeAppIdentifier("C:\\Program Files\\Mozilla Firefox\\firefox.exe");
        var chrome = AppIdentity.ComputeAppIdentifier("C:\\Program Files\\Google\\Chrome\\chrome.exe");

        firefox.Should().NotBe(chrome);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    public void ComputeAppIdentifier_EmptyOrNullPath_ReturnsUnknownSentinel(string? path)
    {
        var identifier = AppIdentity.ComputeAppIdentifier(path);

        identifier.Should().Be(AppIdentity.UnknownIdentifier);
    }

    [Test]
    public void ResolveDisplayName_WithExecutablePath_ReturnsFileNameWithoutExtension()
    {
        var name = AppIdentity.ResolveDisplayName(
            "C:\\Program Files\\Mozilla Firefox\\firefox.exe",
            processName: "firefox");

        name.Should().Be("firefox");
    }

    [Test]
    public void ResolveDisplayName_WithEmptyPath_FallsBackToProcessName()
    {
        var name = AppIdentity.ResolveDisplayName(executablePath: "", processName: "svchost");

        name.Should().Be("svchost");
    }

    [Test]
    public void ResolveDisplayName_WithNullPath_FallsBackToProcessName()
    {
        var name = AppIdentity.ResolveDisplayName(executablePath: null, processName: "svchost");

        name.Should().Be("svchost");
    }
}
