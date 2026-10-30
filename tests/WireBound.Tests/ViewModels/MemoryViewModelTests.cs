using WireBound.Avalonia.Services;
using WireBound.Avalonia.ViewModels;
using WireBound.Core;
using WireBound.Core.Models;
using WireBound.Core.Services;
using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Abstract.Services;
using WireBound.Tests.Fixtures;

namespace WireBound.Tests.ViewModels;

public class MemoryViewModelTests
{
    private static MemoryInfoData Memory => new()
    {
        InstalledBytes = 17000,
        TotalBytes = 16000,
        UsedBytes = 10000,
        AvailableBytes = 6000,
        NonpagedPoolBytes = 1000
    };
    private static ProcessUsageSnapshot Process(int pid, string path, long? privateRam, long resident = 2000) => new()
    {
        ProcessId = pid,
        StartMarker = pid + 100,
        ProcessName = "browser",
        ExecutablePath = path,
        PrivateWorkingSetBytes = privateRam,
        WorkingSetBytes = resident,
        PrivateBytes = 90000
    };
    private static (MemoryViewModel Vm, IWorkloadMemoryService Workloads, INavigationService Navigation, ProcessContextService Context)
        Create(MemoryInfoData? memory = null, IReadOnlyList<ProcessUsageSnapshot>? processes = null,
            IReadOnlyList<MemoryWorkload>? workloads = null)
    {
        var provider = Substitute.For<IMemoryInfoProvider>();
        provider.GetMemoryInfo().Returns(memory ?? Memory);
        var usage = Substitute.For<IProcessUsageService>();
        usage.CaptureAsync(Arg.Any<CancellationToken>(), true)
            .Returns(Task.FromResult(processes ?? (IReadOnlyList<ProcessUsageSnapshot>)[]));
        var workloadService = Substitute.For<IWorkloadMemoryService>();
        workloadService.CaptureAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(workloads ?? (IReadOnlyList<MemoryWorkload>)[]));
        var navigation = Substitute.For<INavigationService>();
        navigation.CurrentView.Returns(Routes.Overview);
        var context = new ProcessContextService();
        var vm = new MemoryViewModel(provider, usage, Substitute.For<IWorkloadHostProvider>(),
            new SynchronousDispatcher(), navigation, context, workloadService);
        navigation.CurrentView.Returns(Routes.Memory);
        return (vm, workloadService, navigation, context);
    }

    [Test]
    public void Attribution_UsesExclusiveResidentPages_AndNeverSubtractsVmOrContainerObservations()
    {
        var hosts = Substitute.For<IWorkloadHostProvider>();
        hosts.IdentifyHost("vmmemwsl", "").Returns(new WorkloadHost("wsl", "WSL", "process name"));
        var roots = MemoryConsumerBuilder.Build(Memory,
            [Process(1, "browser.exe", 2000), new ProcessUsageSnapshot
            { ProcessId = 2, ProcessName = "vmmemwsl", PrivateWorkingSetBytes = 1000, WorkingSetBytes = 3000 }],
            [new("docker", "Docker", 7000, "Container usage", "overlaps VM")], hosts, true);
        roots[0].PhysicalBytes.Should().Be(2000);
        roots[1].PhysicalBytes.Should().Be(1000);
        roots[2].PhysicalBytes.Should().Be(1000);
        roots[3].PhysicalBytes.Should().Be(6000);
        roots.Sum(r => r.PhysicalBytes ?? 0).Should().Be(Memory.UsedBytes);
        roots[1].Children.Single(r => r.Id == "docker").PhysicalBytes.Should().BeNull();
        roots[1].Children.Single(r => r.Id == "docker").ObservedBytes.Should().Be(7000);
    }

    [Test]
    public void AppGroups_UseExactExecutable_KeepAllProcesses_AndMarkPartialMeasurements()
    {
        var roots = MemoryConsumerBuilder.Build(Memory,
            [Process(1, "one/browser.exe", 500), Process(2, "one/browser.exe", null), Process(3, "two/browser.exe", 1000)],
            [], Substitute.For<IWorkloadHostProvider>(), true);
        roots[0].Children.Should().HaveCount(2);
        roots[0].Children[0].PhysicalBytes.Should().Be(1000);
        var partial = roots[0].Children.Single(r => r.Children.Count == 2);
        partial.IsPartial.Should().BeTrue();
        partial.PhysicalDisplay.Should().StartWith("≥ ");
        roots[0].Children.SelectMany(r => r.Processes).Should().HaveCount(3).And.OnlyHaveUniqueItems();
        roots[3].PhysicalBytes.Should().Be(7500);
    }

    [Test]
    public void IncompatibleSamples_DoNotClampOrInventExclusiveOwnership()
    {
        var roots = MemoryConsumerBuilder.Build(Memory, [Process(1, "app", 9500, 10000)], [], Substitute.For<IWorkloadHostProvider>(), true);
        roots[0].PhysicalBytes.Should().BeNull();
        roots[3].PhysicalBytes.Should().BeNull();
        roots[0].Children[0].ObservedBytes.Should().Be(10000);
    }

    [Test]
    public async Task InvalidCapacity_KeepsConsumerObservations_AndShowsUnavailableTotals()
    {
        var (vm, _, _, _) = Create(new MemoryInfoData { TotalBytes = 100, UsedBytes = 90, AvailableBytes = 90 },
            [Process(1, "app", 100)]);
        using (vm)
        {
            await vm.RefreshCommand.ExecuteAsync(null);
            vm.InUse.Should().Be("—");
            vm.Consumers.Single(r => r.Id.StartsWith("app:")).ObservedBytes.Should().Be(2000);
            vm.SampleStatus.Should().Be("Physical RAM unavailable");
        }
    }

    [Test]
    public async Task Filter_FindsNestedPid_AndRestoresExpansionAndSelectionOnRefresh()
    {
        var (vm, _, navigation, context) = Create(processes: [Process(11, "browser", 1000), Process(12, "browser", 1000)]);
        using (vm)
        {
            await vm.RefreshCommand.ExecuteAsync(null);
            var app = vm.Consumers.Single(r => r.Id.StartsWith("app:"));
            await vm.ToggleConsumerCommand.ExecuteAsync(app);
            vm.SelectedConsumer = app;
            await vm.RefreshCommand.ExecuteAsync(null);
            vm.SelectedConsumer!.Id.Should().Be(app.Id);
            vm.Consumers.Should().Contain(r => r.Id == "process:11:111");
            vm.SearchText = "PID 12";
            vm.Consumers.Should().Contain(r => r.Id == "process:12:112").And.NotContain(r => r.Id == "process:11:111");
            vm.SelectedConsumer = vm.Consumers.Single(r => r.Id == "process:12:112");
            vm.ViewRelatedProcessesCommand.Execute(null);
            context.Current!.ConfirmedInstances.Should().ContainSingle().Which.Should().Be(new ProcessInstanceId(12, 112));
            navigation.Received().NavigateTo(Routes.Apps);
            vm.SearchText = "nothing-matches";
            vm.HasNoResults.Should().BeTrue();
        }
    }

    [Test]
    public async Task ContainerProcesses_LoadOnlyOnExpansion_AndRefreshWithNewSnapshot()
    {
        var (vm, workloads, _, _) = Create(workloads:
            [new("docker", "Docker", 1000, "Container usage", "", [new("docker:abc", "database", 1000, "Container usage", "", ContainerId: "abcdef012345")])]);
        workloads.GetContainerProcessesAsync("abcdef012345", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<MemoryWorkload>>([new("inside:1", "postgres", 1000, "Process RSS", "")]));
        using (vm)
        {
            await vm.RefreshCommand.ExecuteAsync(null);
            await workloads.DidNotReceive().GetContainerProcessesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
            await vm.ToggleConsumerCommand.ExecuteAsync(vm.Consumers.Single(r => r.Id == "docker"));
            await vm.ToggleConsumerCommand.ExecuteAsync(vm.Consumers.Single(r => r.Id == "docker:abc"));
            vm.Consumers.Should().Contain(r => r.Name == "postgres" && r.PhysicalBytes == null);
            vm.SelectedConsumer = vm.Consumers.Single(r => r.Id == "inside:1");
            await vm.RefreshCommand.ExecuteAsync(null);
            await workloads.Received(2).GetContainerProcessesAsync("abcdef012345", Arg.Any<CancellationToken>());
            vm.SelectedConsumer!.Id.Should().Be("inside:1");
        }
    }

    [Test]
    public async Task LeavingMemory_CancelsCapture_AndRejectsLateResults()
    {
        var (vm, workloads, navigation, _) = Create();
        var pending = new TaskCompletionSource<IReadOnlyList<MemoryWorkload>>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken capturedToken = default;
        workloads.CaptureAsync(Arg.Any<CancellationToken>()).Returns(call =>
        {
            capturedToken = call.Arg<CancellationToken>();
            return pending.Task;
        });
        using (vm)
        {
            var refresh = vm.RefreshCommand.ExecuteAsync(null);
            navigation.CurrentView.Returns(Routes.Apps);
            navigation.NavigationChanged += Raise.Event<Action<string>>(Routes.Apps);
            capturedToken.IsCancellationRequested.Should().BeTrue();
            pending.SetResult([]);
            await refresh;
            vm.Consumers.Should().BeEmpty();
            vm.IsLoading.Should().BeFalse();
        }
    }
}
