using WireBound.Avalonia.Services;
using WireBound.Avalonia.ViewModels;
using WireBound.Core;
using WireBound.Core.Models;
using WireBound.Core.Helpers;
using WireBound.Core.Services;
using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Abstract.Services;
using WireBound.Tests.Fixtures;

namespace WireBound.Tests.ViewModels;

public class MemoryViewModelTests
{
    [Test]
    public async Task MemorySample_ReconcilesPhysicalCapacity_AndKeepsVmHostOutsideLedger()
    {
        var memory = Substitute.For<IMemoryInfoProvider>();
        memory.GetMemoryInfo().Returns(new MemoryInfoData
        {
            InstalledBytes = 17L * 1024 * 1024 * 1024,
            TotalBytes = 16L * 1024 * 1024 * 1024,
            UsedBytes = 10L * 1024 * 1024 * 1024,
            AvailableBytes = 6L * 1024 * 1024 * 1024,
            NonpagedPoolBytes = 1L * 1024 * 1024 * 1024
        });
        var processUsage = Substitute.For<IProcessUsageService>();
        processUsage.CaptureAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ProcessUsageSnapshot>>(
            [new ProcessUsageSnapshot { ProcessId = 42, StartMarker = 1234,
                ProcessName = "VmmemWSL", WorkingSetBytes = 2L * 1024 * 1024 * 1024 }]));
        var hosts = Substitute.For<IWorkloadHostProvider>();
        hosts.IdentifyHost("VmmemWSL", Arg.Any<string>())
            .Returns(new WorkloadHost("wsl-host", "WSL utility VM host", "Process name: VmmemWSL"));
        var navigation = Substitute.For<INavigationService>();
        navigation.CurrentView.Returns(Routes.Memory);
        var context = new ProcessContextService();
        using var vm = new MemoryViewModel(memory, processUsage, hosts,
            new SynchronousDispatcher(), navigation, context);
        await WaitUntilAsync(() => !vm.IsLoading && vm.ObservedWorkloads.Count == 1);

        vm.NonpagedPool.Should().Be(ByteFormatter.FormatBytes(1L * 1024 * 1024 * 1024));
        vm.Unattributed.Should().Be(ByteFormatter.FormatBytes(9L * 1024 * 1024 * 1024));
        vm.HardwareReserved.Should().Be(ByteFormatter.FormatBytes(1L * 1024 * 1024 * 1024));
        vm.ObservedWorkloads.Should().ContainSingle();
        vm.SelectedWorkload = vm.ObservedWorkloads[0];
        vm.ViewRelatedProcessesCommand.Execute(null);

        context.Current.Should().NotBeNull();
        context.Current!.LikelyInstances.Should().Contain(new ProcessInstanceId(42, 1234));
        navigation.Received(1).NavigateTo(Routes.Apps);
    }

    [Test]
    public async Task InvalidPhysicalSample_DoesNotInventAnAllocation()
    {
        var memory = Substitute.For<IMemoryInfoProvider>();
        memory.GetMemoryInfo().Returns(new MemoryInfoData
        {
            TotalBytes = 100,
            UsedBytes = 90,
            AvailableBytes = 90
        });
        var processUsage = Substitute.For<IProcessUsageService>();
        processUsage.CaptureAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ProcessUsageSnapshot>>([]));
        var navigation = Substitute.For<INavigationService>();
        navigation.CurrentView.Returns(Routes.Memory);
        using var vm = new MemoryViewModel(memory, processUsage,
            Substitute.For<IWorkloadHostProvider>(), new SynchronousDispatcher(),
            navigation, new ProcessContextService());
        await WaitUntilAsync(() => !vm.IsLoading);

        vm.InUse.Should().Be("—");
        vm.Unattributed.Should().Be("—");
        vm.SampleStatus.Should().Be("Physical RAM unavailable");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++) await Task.Delay(10);
        condition().Should().BeTrue();
    }
}
