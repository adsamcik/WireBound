using System.Runtime.Versioning;
using AwesomeAssertions;
using TUnit.Core;
using WireBound.Platform.Abstract.Services;
using WireBound.Platform.Windows.Services;

namespace WireBound.Tests.Platform;

/// <summary>
/// WindowsNetworkCostProvider uses Process.Start() directly for PowerShell execution.
/// Unit testing requires introducing an IPowerShellExecutor abstraction.
/// </summary>
public class WindowsNetworkCostProviderTests
{
    [Test]
    public void Class_ImplementsINetworkCostProvider()
    {
        typeof(WindowsNetworkCostProvider).Should().Implement<INetworkCostProvider>();
    }

    [Test]
    public void Class_HasWindowsPlatformAttribute()
    {
        typeof(WindowsNetworkCostProvider)
            .GetCustomAttributes(typeof(SupportedOSPlatformAttribute), false)
            .Should().ContainSingle()
            .Which.Should().BeOfType<SupportedOSPlatformAttribute>()
            .Which.PlatformName.Should().Be("windows");
    }
}
