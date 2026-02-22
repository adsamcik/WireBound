using Microsoft.Extensions.Time.Testing;
using WireBound.Avalonia.Services;
using WireBound.Core.Data;
using WireBound.Core.Models;
using WireBound.Core.Services;
using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Abstract.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace WireBound.Tests.Services;

public class ResourceInsightsServiceTests
{
    private static (ResourceInsightsService service, IProcessResourceProvider provider, FakeTimeProvider fakeTime) CreateService()
    {
        var provider = Substitute.For<IProcessResourceProvider>();
        var categoryService = Substitute.For<IAppCategoryService>();
        categoryService.GetCategory(Arg.Any<string>()).Returns("Other");
        categoryService.GetCategory("chrome").Returns("Web Browsers");
        categoryService.GetCategory("code").Returns("Development Tools");

        var options = new DbContextOptionsBuilder<WireBoundDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        // Ensure DB is created
        using (var ctx = new WireBoundDbContext(options))
            ctx.Database.EnsureCreated();

        var services = new ServiceCollection();
        services.AddScoped(_ => new WireBoundDbContext(options));
        var serviceProvider = services.BuildServiceProvider();

        var fakeTime = new FakeTimeProvider();
        var service = new ResourceInsightsService(provider, categoryService, serviceProvider, fakeTime);
        return (service, provider, fakeTime);
    }

    [Test]
    public async Task GetCurrentByAppAsync_GroupsByExecutablePath()
    {
        var (service, provider, _) = CreateService();

        // Simulate 3 chrome processes
        provider.GetProcessResourceDataAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ProcessResourceData>
            {
                new() { ProcessId = 1, ProcessName = "chrome", ExecutablePath = @"C:\chrome.exe", PrivateBytes = 500_000_000, CpuTimeTicks = 1000 },
                new() { ProcessId = 2, ProcessName = "chrome", ExecutablePath = @"C:\chrome.exe", PrivateBytes = 300_000_000, CpuTimeTicks = 500 },
                new() { ProcessId = 3, ProcessName = "code", ExecutablePath = @"C:\code.exe", PrivateBytes = 200_000_000, CpuTimeTicks = 200 },
            });

        var apps = await service.GetCurrentByAppAsync();

        apps.Count.Should().Be(2);
        var chrome = apps.FirstOrDefault(a => a.AppName == "chrome");
        chrome.Should().NotBeNull();
        chrome!.ProcessCount.Should().Be(2);
        // First call: no EMA smoothing delta, so private bytes = raw sum
        chrome.PrivateBytes.Should().Be(800_000_000);
    }

    [Test]
    public async Task GetCurrentByAppAsync_ReturnsEmptyForNoProcesses()
    {
        var (service, provider, _) = CreateService();

        provider.GetProcessResourceDataAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ProcessResourceData>());

        var apps = await service.GetCurrentByAppAsync();

        apps.Count.Should().Be(0);
    }

    [Test]
    public async Task GetCategoryBreakdown_GroupsByCategory()
    {
        var (service, provider, _) = CreateService();

        provider.GetProcessResourceDataAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ProcessResourceData>
            {
                new() { ProcessId = 1, ProcessName = "chrome", ExecutablePath = @"C:\chrome.exe", PrivateBytes = 500_000_000, CpuTimeTicks = 1000 },
                new() { ProcessId = 2, ProcessName = "code", ExecutablePath = @"C:\code.exe", PrivateBytes = 200_000_000, CpuTimeTicks = 200 },
            });

        var apps = await service.GetCurrentByAppAsync();
        var categories = service.GetCategoryBreakdown(apps);

        categories.Count.Should().BeGreaterThanOrEqualTo(2);
    }

    [Test]
    public async Task CpuPercent_SecondCallComputesDelta()
    {
        var (service, provider, fakeTime) = CreateService();

        // First call: establishes baseline
        provider.GetProcessResourceDataAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ProcessResourceData>
            {
                new() { ProcessId = 1, ProcessName = "busy", ExecutablePath = @"C:\busy.exe", PrivateBytes = 100_000_000, CpuTimeTicks = 0 },
            });

        var first = await service.GetCurrentByAppAsync();
        first.First().CpuPercent.Should().Be(0); // No delta on first call

        // Second call: simulate CPU work (1 second of full-core CPU time in ticks)
        provider.GetProcessResourceDataAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ProcessResourceData>
            {
                new() { ProcessId = 1, ProcessName = "busy", ExecutablePath = @"C:\busy.exe", PrivateBytes = 100_000_000, CpuTimeTicks = TimeSpan.TicksPerSecond },
            });

        fakeTime.Advance(TimeSpan.FromMilliseconds(100));

        var second = await service.GetCurrentByAppAsync();
        // CPU% should be > 0 now since there's a delta
        second.First().CpuPercent.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task Smoothing_SecondCallAppliesEma()
    {
        var (service, provider, _) = CreateService();

        // First call: 1 GB
        provider.GetProcessResourceDataAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ProcessResourceData>
            {
                new() { ProcessId = 1, ProcessName = "app", ExecutablePath = @"C:\app.exe", PrivateBytes = 1_000_000_000, CpuTimeTicks = 0 },
            });

        var first = await service.GetCurrentByAppAsync();
        var firstBytes = first.First().PrivateBytes;
        firstBytes.Should().Be(1_000_000_000); // First sample = raw value

        // Second call: spike to 2 GB — EMA should smooth it (not jump to 2 GB instantly)
        provider.GetProcessResourceDataAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ProcessResourceData>
            {
                new() { ProcessId = 1, ProcessName = "app", ExecutablePath = @"C:\app.exe", PrivateBytes = 2_000_000_000, CpuTimeTicks = 0 },
            });

        var second = await service.GetCurrentByAppAsync();
        var secondBytes = second.First().PrivateBytes;
        // With α_up=0.3: smoothed = 1_000_000_000 * 0.7 + 2_000_000_000 * 0.3 = 1_300_000_000
        secondBytes.Should().Be(1_300_000_000);
    }
}
