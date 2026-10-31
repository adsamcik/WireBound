using System.Collections.Concurrent;
using System.Text.Json;
using WireBound.Avalonia.Services;
using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Tests.Services;

public class RancherMemoryServiceTests
{
    private const string ContainerId = "abcdef012345";
    private const string Socket = "/run/k3s/containerd/containerd.sock";
    private const string DefaultEndpoint = "unix:///var/run/docker.sock";
    private const string RancherEndpoint = "unix:///home/user/.rd/docker.sock";
    private const string Stats = """{"ID":"abcdef012345","Name":"database","MemUsage":"1.5GiB / 8GiB"}""";
    private static readonly RancherToolPaths Tools = new("rdctl", "rd-nerdctl", "rd-docker");

    private sealed class Fixture
    {
        public string? Backend = """{"vmState":"STARTED","locked":false}""";
        public string Engine = "containerd";
        public string Namespaces = "default\nk8s.io\n";
        public string? DockerEndpoint;
        public string? DefaultStats;
        public string DefaultEngineId = "other-engine";
        public string RancherEngineId = "rancher-engine";
        public Func<WorkloadCommand, CancellationToken, Task<string?>>? Nerdctl;
        public readonly ConcurrentQueue<WorkloadCommand> Commands = new();
        public WorkloadMemoryService Service { get; }

        public Fixture(Func<string, string?>? environment = null)
        {
            var hosts = Substitute.For<IWorkloadHostProvider>();
            hosts.GetVirtualMachinesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<MemoryWorkload>>([]));
            Service = new(hosts, RunAsync, () => Tools, environment ?? (_ => null));
        }

        private Task<string?> RunAsync(WorkloadCommand command, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Commands.Enqueue(command);
            var args = command.Arguments;
            string? text = null;
            if (command.Executable == Tools.Rdctl)
                text = args.Contains("/v1/backend_state") ? Backend : JsonSerializer.Serialize(new { containerEngine = new { name = Engine } });
            else if (command.Executable == Tools.Nerdctl)
            {
                if (Nerdctl is not null) return Nerdctl(command, token);
                text = args.Contains("namespace") ? Namespaces : args.Contains("stats") ? Stats : "PID RSS COMMAND\n10 2048 postgres\n";
            }
            else if (args.Contains("context")) text = args.Contains("rancher-desktop") ? RancherEndpoint : DockerEndpoint;
            else if (args.Contains("info"))
                text = JsonSerializer.Serialize(new { ID = command.Executable == Tools.Docker ? RancherEngineId : DefaultEngineId, Name = "rancher-desktop" });
            else if (args.Contains("stats")) text = command.Executable == Tools.Docker ? Stats : DefaultStats;
            else if (args.Contains("top")) text = "PID RSS COMMAND\n11 1024 worker\n";
            return Task.FromResult(text);
        }
    }

    [Test]
    [Arguments("STOPPED", false)]
    [Arguments("STARTING", false)]
    [Arguments("ERROR", false)]
    [Arguments("STARTED", true)]
    public async Task InactiveOrLockedRancher_DoesNotInvokeItsEngine(string state, bool locked)
    {
        var fixture = new Fixture { Backend = JsonSerializer.Serialize(new { vmState = state, locked }) };
        (await fixture.Service.CaptureAsync(default)).Should().BeEmpty();
        fixture.Commands.Should().NotContain(c => c.Executable == Tools.Nerdctl || c.Executable == Tools.Docker);
        fixture.Commands.Where(c => c.Executable == Tools.Rdctl).Should().ContainSingle()
            .Which.Arguments.Should().StartWith(["--host", "127.0.0.1"]);
    }

    [Test]
    [Arguments("STARTED")]
    [Arguments("DISABLED")]
    public async Task Containerd_CapturesEveryNamespaceWithoutCollidingIds(string state)
    {
        var fixture = new Fixture { Backend = JsonSerializer.Serialize(new { vmState = state, locked = false }) };
        var rows = await fixture.Service.CaptureAsync(default);
        rows.Should().HaveCount(2);
        rows.Select(r => r.Id).Should().OnlyHaveUniqueItems();
        rows.Select(r => r.ContainerContext!.Namespace).Should().BeEquivalentTo(["default", "k8s.io"]);
        rows.Should().OnlyContain(r => r.Bytes == 1610612736L && r.ContainerId == ContainerId && r.ContainerContext!.Runtime == ContainerRuntime.RancherContainerd);
        foreach (var command in fixture.Commands.Where(c => c.Executable == Tools.Nerdctl))
        {
            command.Arguments.Should().StartWith(["--address", Socket]);
            command.Environment!["RD_WSL_DISTRO"].Should().Be("rancher-desktop");
            command.Environment["CONTAINERD_ADDRESS"].Should().Be(Socket);
            command.Environment["RD_NERDCTL"].Should().Be("/usr/local/bin/nerdctl");
        }
    }

    [Test]
    public async Task Containerd_AcceptsNamedContainerdIdsAsWellAsDockerStyleHashes()
    {
        var fixture = new Fixture();
        fixture.Nerdctl = (command, _) => Task.FromResult<string?>(command.Arguments.Contains("namespace") ? "default\n"
            : command.Arguments.Contains("stats") ? """{"ID":"buildkit-worker_1","Name":"builder","MemUsage":"20MiB / 8GiB"}"""
            : "PID RSS COMMAND\n10 2048 buildkitd\n");
        var container = (await fixture.Service.CaptureAsync(default)).Single();
        container.ContainerId.Should().Be("buildkit-worker_1");
        (await fixture.Service.GetContainerProcessesAsync(container.ContainerId!, default, container.ContainerContext)).Should().ContainSingle()
            .Which.Name.Should().Be("buildkitd");
    }

    [Test]
    public async Task Containerd_DrilldownKeepsItsNamespaceAndDoesNotUseDocker()
    {
        var fixture = new Fixture();
        var container = (await fixture.Service.CaptureAsync(default)).Single(r => r.ContainerContext!.Namespace == "k8s.io");
        var rows = await fixture.Service.GetContainerProcessesAsync(container.ContainerId!, default, container.ContainerContext);
        rows.Should().ContainSingle().Which.Bytes.Should().Be(2097152);
        rows[0].Detail.Should().Contain("namespace k8s.io");
        fixture.Commands.Single(c => c.Arguments.Contains("top")).Arguments.Should()
            .Equal("--address", Socket, "--namespace", "k8s.io", "top", ContainerId, "-eo", "pid,rss,comm");
        fixture.Engine = "moby";
        var topCalls = fixture.Commands.Count(c => c.Arguments.Contains("top"));
        (await fixture.Service.GetContainerProcessesAsync(container.ContainerId!, default, container.ContainerContext)).Single().Metric.Should().Be("Unavailable");
        fixture.Commands.Count(c => c.Arguments.Contains("top")).Should().Be(topCalls);
    }

    [Test]
    public async Task Containerd_IgnoresInvalidNamespacesAndKeepsHealthyPartialReadings()
    {
        var fixture = new Fixture { Namespaces = "default\nk8s.io\n--address=tcp://remote\nnamespace with spaces\n" };
        fixture.Nerdctl = (command, _) => Task.FromResult<string?>(command.Arguments.Contains("namespace") ? fixture.Namespaces
            : command.Arguments.Contains("k8s.io") ? "not-json" : Stats);
        var rows = await fixture.Service.CaptureAsync(default);
        rows.Should().ContainSingle().Which.ContainerContext!.Namespace.Should().Be("default");
        fixture.Commands.Should().NotContain(c => c.Arguments.Any(a => a.Contains("tcp://remote") || a == "namespace with spaces"));
    }

    [Test]
    public async Task Moby_DeduplicatesAliasesOfTheActiveDockerEngine()
    {
        var fixture = new Fixture { Engine = "moby", DockerEndpoint = DefaultEndpoint, DefaultEngineId = "same-engine", RancherEngineId = "same-engine" };
        var rows = await fixture.Service.CaptureAsync(default);
        rows.Should().ContainSingle().Which.ContainerContext!.Runtime.Should().Be(ContainerRuntime.RancherMoby);
        fixture.Commands.Count(c => c.Arguments.Contains("stats")).Should().Be(1);
        var container = rows[0];
        await fixture.Service.GetContainerProcessesAsync(container.ContainerId!, default, container.ContainerContext);
        var top = fixture.Commands.Single(c => c.Arguments.Contains("top"));
        top.Executable.Should().Be(Tools.Docker);
        top.Arguments.Should().StartWith(["--host", RancherEndpoint]);
        top.Environment!["DOCKER_CONTEXT"].Should().BeNull();
    }

    [Test]
    public async Task MobyAndAnIndependentDockerEngine_BothKeepTheirReadings()
    {
        var fixture = new Fixture { Engine = "moby", DockerEndpoint = DefaultEndpoint, DefaultStats = Stats };
        var rows = await fixture.Service.CaptureAsync(default);
        rows.Should().HaveCount(2);
        rows.Select(r => r.Id).Should().OnlyHaveUniqueItems();
        rows.Select(r => r.ContainerContext!.Runtime).Should().BeEquivalentTo([ContainerRuntime.Docker, ContainerRuntime.RancherMoby]);
    }

    [Test]
    public async Task DockerDrilldown_UsesItsSampledEndpointEvenAfterTheCurrentContextChanges()
    {
        var fixture = new Fixture { Backend = null, DockerEndpoint = DefaultEndpoint, DefaultStats = Stats };
        var row = (await fixture.Service.CaptureAsync(default)).Single();
        fixture.DockerEndpoint = "tcp://remote:2375";
        await fixture.Service.GetContainerProcessesAsync(row.ContainerId!, default, row.ContainerContext);
        fixture.Commands.Single(c => c.Arguments.Contains("top")).Arguments.Should().StartWith(["--host", DefaultEndpoint]);
    }

    [Test]
    public async Task RemoteEndpointsAndInvalidReferences_AreNotQueried()
    {
        var fixture = new Fixture(name => name == "DOCKER_HOST" ? "tcp://remote:2375" : null) { Backend = null };
        (await fixture.Service.CaptureAsync(default)).Should().BeEmpty();
        await fixture.Service.GetContainerProcessesAsync(ContainerId, default, new(ContainerRuntime.Docker, "ssh://remote"));
        await fixture.Service.GetContainerProcessesAsync("--invalid/id", default, new(ContainerRuntime.RancherContainerd, Socket, "default"));
        fixture.Commands.Should().NotContain(c => c.Arguments.Contains("stats") || c.Arguments.Contains("top"));
    }

    [Test]
    public async Task Containerd_UserCancellationIsPropagated()
    {
        var fixture = new Fixture();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Nerdctl = async (_, token) => { started.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return null; };
        using var cancel = new CancellationTokenSource();
        var capture = fixture.Service.CaptureAsync(cancel.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancel.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await capture);
    }
}
