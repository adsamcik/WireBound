using WireBound.Avalonia.Services;
using WireBound.Avalonia.ViewModels;
using WireBound.Core;
using WireBound.Core.Models;
using WireBound.Core.Services;
using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Abstract.Helpers;
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
            IReadOnlyList<MemoryWorkload>? workloads = null, IWorkloadHostProvider? hosts = null, IAppIconService? icons = null)
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
        var vm = new MemoryViewModel(provider, usage, hosts ?? Substitute.For<IWorkloadHostProvider>(),
            new SynchronousDispatcher(), navigation, context, workloadService, icons ?? Substitute.For<IAppIconService>());
        navigation.CurrentView.Returns(Routes.Memory);
        return (vm, workloadService, navigation, context);
    }

    [Test]
    public async Task Icons_ShareOneExecutableLookupAcrossAppGroupAndProcessDetails()
    {
        var icons = Substitute.For<IAppIconService>();
        icons.GetIconPathAsync("browser.exe", AppIdentity.ComputeAppIdentifier("browser.exe"), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("cached.png"));
        var (vm, _, _, _) = Create(processes: [Process(1, "browser.exe", 1000), Process(2, "browser.exe", 500)], icons: icons);
        using (vm)
        {
            await vm.RefreshCommand.ExecuteAsync(null);
            var app = vm.Consumers.Single(r => r.Id.StartsWith("app:"));
            app.IconPath.Should().Be("cached.png");
            app.Children.Should().OnlyContain(r => r.IconPath == "cached.png");
            await icons.Received(1).GetIconPathAsync("browser.exe", AppIdentity.ComputeAppIdentifier("browser.exe"), Arg.Any<CancellationToken>());
        }
    }

    [Test]
    public async Task Icons_MissingOrFailedLookupsKeepMemoryDataAndFallbacks()
    {
        var icons = Substitute.For<IAppIconService>();
        icons.GetIconPathAsync("inaccessible.exe", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<string?>(new IOException("Unavailable icon")));
        var (vm, _, _, _) = Create(processes: [Process(1, "inaccessible.exe", 1000), Process(2, "", 500)],
            workloads: [new("vm:1", "Guest", 2000, "Assigned to VM", ""),
                new("docker:1", "Container", 900, "Container usage", "", ContainerId: "1")], icons: icons);
        using (vm)
        {
            await vm.RefreshCommand.ExecuteAsync(null);
            vm.SampleStatus.Should().StartWith("Sampled");
            vm.IsLoading.Should().BeFalse();
            vm.Consumers.Should().OnlyContain(r => r.IconPath == null);
            vm.Consumers.Single(r => r.Name == "browser" && r.PhysicalBytes == 1000).IconKey.Should().Be("WbEntityApp");
            vm.Consumers.Single(r => r.Id == "vm:1").IconKey.Should().Be("WbAdapterVirtual");
            vm.Consumers.Single(r => r.Id == "docker:1").IconKey.Should().Be("WbNavApps");
            vm.Consumers.Single(r => r.Id == "nonpaged").IconKey.Should().Be("WbNavSystem");
            vm.Consumers.Single(r => r.Id == "other").IconKey.Should().Be("WbEntityMemory");
            vm.Consumers.Sum(r => r.PhysicalBytes ?? 0).Should().Be(Memory.UsedBytes);
            await icons.DidNotReceive().GetIconPathAsync("", Arg.Any<string>(), Arg.Any<CancellationToken>());
        }
    }

    [Test]
    public async Task Icons_LateLookupDoesNotBlockNewRefresh_OrUpdateTheReplacedSample()
    {
        var pendingIcon = new TaskCompletionSource<string?>();
        var lookupStarted = new TaskCompletionSource();
        var lookups = 0;
        var icons = Substitute.For<IAppIconService>();
        icons.GetIconPathAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => { lookupStarted.TrySetResult(); return ++lookups == 1 ? pendingIcon.Task : Task.FromResult<string?>("current.png"); });
        var (vm, _, _, _) = Create(processes: [Process(1, "browser.exe", 1000)], icons: icons);
        using (vm)
        {
            var refresh = vm.RefreshCommand.ExecuteAsync(null);
            await lookupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            vm.SampleStatus.Should().StartWith("Sampled");
            vm.IsLoading.Should().BeFalse();
            var row = vm.Consumers.Single(r => r.Id.StartsWith("app:"));
            vm.RefreshCommand.CanExecute(null).Should().BeTrue();
            await vm.RefreshCommand.ExecuteAsync(null);
            var currentRow = vm.Consumers.Single(r => r.Id.StartsWith("app:"));
            currentRow.Should().NotBeSameAs(row);
            currentRow.IconPath.Should().Be("current.png");
            pendingIcon.SetResult("late.png");
            await refresh;
            row.IconPath.Should().BeNull();
            currentRow.IconPath.Should().Be("current.png");
        }
    }

    [Test]
    public async Task MemoryBar_CombinesAppProcesses_AccountsForAllCapacity_AndExcludesOverlappingObservations()
    {
        var (vm, _, _, _) = Create(processes:
            [Process(1, "one", 1000), Process(2, "one", 1000), Process(3, "small", 100)],
            workloads: [new("docker", "Docker", 9000, "Container usage", "overlaps host RAM")]);
        using (vm)
        {
            await vm.RefreshCommand.ExecuteAsync(null);
            vm.MemorySegments.Where(s => s.ConsumerId == "app:path:one").Should().ContainSingle().Which.Bytes.Should().Be(2000);
            vm.MemorySegments.Single(s => s.Label == "Other apps").Bytes.Should().Be(100);
            vm.MemorySegments.Should().NotContain(s => s.ConsumerId == "docker");
            vm.MemorySegments.Single(s => s.Label == "Other apps").ConsumerId.Should().Be("app:path:small");
            vm.MemorySegments.Sum(s => s.Bytes).Should().Be(17000);
            vm.MemorySegments.Sum(s => s.Percent).Should().BeApproximately(100, 0.00001);
            vm.MemorySegments.Single(s => s.Label == "Available").ConsumerId.Should().BeNull();
            vm.MemorySegments.Single(s => s.Label == "Reserved").Bytes.Should().Be(1000);
        }
    }

    [Test]
    public async Task MemoryBarClick_RevealsAndExpandsAppGroup_EvenWhenFilteredOrCollapsed()
    {
        var (vm, _, navigation, _) = Create(processes: [Process(1, "one", 1000), Process(2, "one", 1000)]);
        using (vm)
        {
            await vm.RefreshCommand.ExecuteAsync(null);
            var segment = vm.MemorySegments.Single(s => s.ConsumerId == "app:path:one");
            vm.Consumers.Single(r => r.Id == "app:path:one").IsExpanded.Should().BeFalse();
            vm.SearchText = "no-match";
            MemoryConsumerRow? revealed = null;
            vm.ConsumerRevealRequested += row => revealed = row;
            vm.RevealMemoryConsumerCommand.Execute(segment);
            vm.SearchText.Should().BeEmpty();
            vm.SelectedConsumer!.Id.Should().Be("app:path:one");
            vm.SelectedConsumer.IsExpanded.Should().BeTrue();
            vm.Consumers.Should().Contain(r => r.Id == "process:1:101").And.Contain(r => r.Id == "process:2:102");
            revealed.Should().BeSameAs(vm.SelectedConsumer);
            navigation.DidNotReceive().NavigateTo(Arg.Any<string>());
        }
    }

    [Test]
    public async Task MemoryBar_AllSegmentLabelsHaveReadableContrast()
    {
        var hosts = Substitute.For<IWorkloadHostProvider>();
        hosts.IdentifyHost("browser", "host").Returns(new WorkloadHost("vm", "VM host", "process name"));
        var (vm, _, _, _) = Create(processes: [.. Enumerable.Range(1, 6).Select(i => Process(i, $"app{i}", 600)),
            Process(7, "small", 100), Process(8, "host", 100)], hosts: hosts);
        using (vm)
        {
            await vm.RefreshCommand.ExecuteAsync(null);
            foreach (var segment in vm.MemorySegments)
            {
                static double Luminance(string hex)
                {
                    var color = SkiaSharp.SKColor.Parse(hex);
                    static double Channel(byte c) => c <= 10 ? c / 3294.6 : Math.Pow((c / 255d + .055) / 1.055, 2.4);
                    return .2126 * Channel(color.Red) + .7152 * Channel(color.Green) + .0722 * Channel(color.Blue);
                }
                var background = Luminance(segment.Color);
                var foreground = Luminance(segment.UseLightText ? "#F4F7FA" : "#0B1017");
                var contrast = (Math.Max(background, foreground) + .05) / (Math.Min(background, foreground) + .05);
                contrast.Should().BeGreaterThanOrEqualTo(4.5, $"{segment.Label} must remain readable on {segment.Color}");
            }
        }
    }

    [Test]
    public async Task MemoryBar_KeepsAppColorsWhenUsageRankingChanges()
    {
        var processes = new List<ProcessUsageSnapshot> { Process(1, "one", 1000), Process(2, "two", 1500) };
        var (vm, _, _, _) = Create(processes: processes);
        using (vm)
        {
            await vm.RefreshCommand.ExecuteAsync(null);
            var firstColors = vm.MemorySegments.Where(s => s.ConsumerId?.StartsWith("app:") == true).ToDictionary(s => s.ConsumerId!, s => s.Color);
            processes[0] = Process(1, "one", 1900);
            processes[1] = Process(2, "two", 500);
            await vm.RefreshCommand.ExecuteAsync(null);
            vm.MemorySegments.Where(s => s.ConsumerId?.StartsWith("app:") == true)
                .Should().OnlyContain(s => s.Color == firstColors[s.ConsumerId!]);
        }
    }

    [Test]
    public async Task MemoryBar_IncompatibleAttribution_FallsBackToInUseWithoutInventingAppSlices()
    {
        var (vm, _, _, _) = Create(processes: [Process(1, "one", 9500, 10000)]);
        using (vm)
        {
            await vm.RefreshCommand.ExecuteAsync(null);
            vm.MemorySegments.Where(s => s.ConsumerId is not null).Should().ContainSingle().Which.Bytes.Should().Be(10000);
            vm.MemorySegments.Should().NotContain(s => s.ConsumerId == "app:path:one");
            vm.MemorySegments.Sum(s => s.Bytes).Should().Be(17000);
        }
    }

    [Test]
    public async Task ConsumerList_InterleavesAppsGuestsContainersAndKernelRam_InDescendingUsage()
    {
        var hosts = Substitute.For<IWorkloadHostProvider>();
        hosts.IdentifyHost("VmmemWSL", "").Returns(new WorkloadHost("wsl", "WSL host", "process name"));
        var (vm, _, _, _) = Create(processes:
            [Process(1, "one", 1000), Process(2, "one", 800), Process(3, "two", 700),
                new ProcessUsageSnapshot { ProcessId = 4, ProcessName = "VmmemWSL", PrivateWorkingSetBytes = 2000, WorkingSetBytes = 2100 }],
            workloads:
            [new("guest", "Development VM", 3000, "Assigned to VM", ""),
                new("docker", "Docker", 1800, "Container usage", "",
                    [new("docker:large", "database", 1600, "Container usage", "", ContainerId: "abcdef012345"),
                        new("docker:small", "web", 200, "Container usage", "", ContainerId: "abcdef012346")]),
                new("unknown", "WSL distro", null, "WSL distribution", "")], hosts: hosts);
        using (vm)
        {
            await vm.RefreshCommand.ExecuteAsync(null);
            vm.Consumers.Select(r => r.Id).Should().Equal("other", "guest", "host:4:0", "app:path:one",
                "docker:large", "nonpaged", "app:path:two", "docker:small", "unknown");
            vm.Consumers.Should().OnlyContain(r => r.Depth == 0);
            vm.Consumers.Sum(r => r.PhysicalBytes ?? 0).Should().Be(Memory.UsedBytes);
            vm.Consumers.Single(r => r.Id == "docker:large").UsageMetric.Should().Be("Docker usage");
            vm.Consumers.Single(r => r.Id == "guest").UsageMetric.Should().Be("Assigned to VM");
        }
    }

    [Test]
    public async Task ConsumerList_PartialAppUsesCompleteWorkingSet_AndRetainsPrivateRamInDetails()
    {
        var (vm, _, _, _) = Create(processes:
            [Process(1, "partial", 100), Process(2, "partial", null), Process(3, "complete", 1800)]);
        using (vm)
        {
            await vm.RefreshCommand.ExecuteAsync(null);
            var partial = vm.Consumers.Single(r => r.Id == "app:path:partial");
            partial.UsageBytes.Should().Be(4000);
            partial.UsageMetric.Should().Be("Working set");
            partial.MeasurementDetails.Should().Contain(partial.PhysicalDisplay).And.Contain(partial.ObservedDisplay);
            vm.Consumers.IndexOf(partial).Should().BeLessThan(vm.Consumers.IndexOf(vm.Consumers.Single(r => r.Id == "app:path:complete")));
            var segment = vm.MemorySegments.Single(s => s.Label == "Other apps");
            segment.ConsumerId.Should().Be(partial.Id);
            vm.RevealMemoryConsumerCommand.Execute(segment);
            vm.SelectedConsumer.Should().BeSameAs(partial);
            vm.Consumers.Should().Contain(r => r.Id == "process:2:102");
        }
    }

    [Test]
    public async Task ConsumerList_RefreshResortsConsumers_AndKeepsSelection()
    {
        var processes = new List<ProcessUsageSnapshot> { Process(1, "one", 1000), Process(2, "two", 1500) };
        var (vm, _, _, _) = Create(processes: processes);
        using (vm)
        {
            await vm.RefreshCommand.ExecuteAsync(null);
            vm.SelectedConsumer = vm.Consumers.Single(r => r.Id == "app:path:one");
            processes[0] = Process(1, "one", 1900);
            processes[1] = Process(2, "two", 500);
            await vm.RefreshCommand.ExecuteAsync(null);
            vm.SelectedConsumer!.Id.Should().Be("app:path:one");
            vm.Consumers.Where(r => r.Id.StartsWith("app:")).Select(r => r.Id)
                .Should().Equal("app:path:one", "app:path:two");
        }
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
            vm.MemorySegments.Should().BeEmpty();
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
            vm.Consumers.Single(r => r.Id == "docker:abc").Depth.Should().Be(0);
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
