using System.Diagnostics;
using System.Runtime.Versioning;
using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Windows.Services;

namespace WireBound.Tests.Platform;

[SupportedOSPlatform("windows")]
public class WindowsServiceHostSnapshotTests
{
    private static ProcessResourceData Host(string? path = null, long marker = 100) => new()
    {
        ProcessId = 42,
        StartMarker = marker,
        ProcessName = "svchost",
        ExecutablePath = path ?? Path.Combine(Environment.SystemDirectory, "svchost.exe"),
        WorkingSetBytes = 4000,
        PrivateWorkingSetBytes = 2000,
        PrivateBytes = 9000,
        CpuTimeTicks = 123
    };

    private static readonly IReadOnlyDictionary<int, IReadOnlyList<HostedServiceInfo>> Services =
        new Dictionary<int, IReadOnlyList<HostedServiceInfo>> { [42] = [new("Dnscache", "DNS Client"), new("Dhcp", "DHCP Client")] };

    [Test]
    public void ServiceMapping_RetainsWholeHostMetricsAndExactServices()
    {
        var sample = Host();
        var host = WindowsServiceHostSnapshot.Enrich([sample], Services, _ => true).Single();
        host.ServiceHost!.Services.Should().BeEquivalentTo(Services[42]);
        host.ProcessId.Should().Be(42);
        host.StartMarker.Should().Be(100);
        host.WorkingSetBytes.Should().Be(4000);
        host.PrivateWorkingSetBytes.Should().Be(2000);
        host.PrivateBytes.Should().Be(9000);
        host.CpuTimeTicks.Should().Be(123);
    }

    [Test]
    [Arguments(100, false)]
    [Arguments(0, true)]
    public void ReusedOrUnknownGeneration_DoesNotInheritServiceNames(long marker, bool sameInstance)
    {
        var host = WindowsServiceHostSnapshot.Enrich([Host(marker: marker)], Services, _ => sameInstance).Single();
        host.ServiceHost.Should().NotBeNull();
        host.ServiceHost!.Services.Should().BeEmpty();
    }

    [Test]
    public void ServiceLookupFailure_KeepsSystemHostsIdentified()
    {
        var host = WindowsServiceHostSnapshot.Enrich([Host()], new Dictionary<int, IReadOnlyList<HostedServiceInfo>>(), _ => true).Single();
        host.ServiceHost.Should().NotBeNull();
        host.ServiceHost!.Services.Should().BeEmpty();
    }

    [Test]
    public void MatchingNameOutsideWindows_IsNotRelabelledAsSystemHost()
    {
        var app = Host("C:/Tools/svchost.exe");
        WindowsServiceHostSnapshot.Enrich([app], new Dictionary<int, IReadOnlyList<HostedServiceInfo>>(), _ => true).Single().Should().BeSameAs(app);
        var registeredService = WindowsServiceHostSnapshot.Enrich([app], Services, _ => true).Single();
        registeredService.ServiceHost!.IsSharedHost.Should().BeFalse("SCM identifies a service without asserting it is the Windows shared host");
        registeredService.WindowsProgram.Should().BeNull();
        var inaccessible = Host("");
        WindowsServiceHostSnapshot.Enrich([inaccessible], Services, _ => false).Single().ServiceHost.Should().BeNull();
        WindowsServiceHostSnapshot.Enrich([inaccessible], Services, _ => true).Single().ServiceHost!.Services.Should().HaveCount(2);
    }

    [Test]
    public void StandaloneService_PreservesItsProcessAndServiceIdentity()
    {
        var sample = new ProcessResourceData { ProcessId = 42, StartMarker = 100, ProcessName = "spoolsv", WorkingSetBytes = 4000 };
        var services = new Dictionary<int, IReadOnlyList<HostedServiceInfo>> { [42] = [new("Spooler", "Print Spooler")] };
        var result = WindowsServiceHostSnapshot.Enrich([sample], services, _ => true).Single();
        result.ServiceHost!.IsSharedHost.Should().BeFalse();
        result.ServiceHost.Services.Should().ContainSingle().Which.Name.Should().Be("Spooler");
        result.WorkingSetBytes.Should().Be(4000);
        WindowsServiceHostSnapshot.Enrich([sample], services, _ => false).Single().ServiceHost.Should().BeNull();
    }

    [Test]
    public void NativeImagePath_IsAttachedOnlyToTheMatchingGeneration()
    {
        var sample = new ProcessResourceData { ProcessId = 42, StartMarker = 100, ProcessName = "dwm" };
        var paths = new Dictionary<int, string> { [42] = "C:/Windows/System32/dwm.exe" };
        var services = new Dictionary<int, IReadOnlyList<HostedServiceInfo>>();
        WindowsServiceHostSnapshot.Enrich([sample], services, _ => true, imagePaths: paths).Single().ExecutablePath.Should().Be(paths[42]);
        WindowsServiceHostSnapshot.Enrich([sample], services, _ => false, imagePaths: paths).Single().Should().BeSameAs(sample);
    }

    [Test]
    public void LimitedInformationPathQuery_RejectsWrongGeneration()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var process = Process.GetCurrentProcess();
        var marker = process.StartTime.ToUniversalTime().Ticks;
        WindowsProcessResourceProvider.ReadExecutablePath(process.Id, marker).Should().Be(Environment.ProcessPath);
        WindowsProcessResourceProvider.ReadExecutablePath(process.Id, marker + 1).Should().BeEmpty();
    }
}
