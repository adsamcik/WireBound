using WireBound.Avalonia.Services;
using WireBound.Platform.Abstract.Helpers;
using System.Text;

namespace WireBound.Tests.Services;

public class WorkloadMemoryServiceTests
{
    [Test]
    public void WslOutput_DetectsUtf8AndUtf16WithoutCorruptingUnicodeNames()
    {
        const string output = "Ubuntu-研究\r\n";
        LocalCommandRunner.DecodeOutput(Encoding.Unicode.GetBytes(output)).Should().Be(output);
        LocalCommandRunner.DecodeOutput(Encoding.UTF8.GetBytes(output)).Should().Be(output);
    }

    [Test]
    public void DockerStats_PreservesUnknownMemory_AndParsesBinaryAndDecimalUnits()
    {
        var rows = WorkloadMemoryService.ParseDockerStats("""
            {"ID":"abcdef012345","Name":"database","MemUsage":"1.5GiB / 8GiB"}
            {"ID":"abcdef012346","Name":"web","MemUsage":"20MB / 8GiB"}
            {"ID":"abcdef012347","Name":"unknown","MemUsage":"-- / --"}
            """);
        rows[0].Bytes.Should().Be(1610612736L);
        rows[1].Bytes.Should().Be(20000000L);
        rows[2].Bytes.Should().BeNull();
    }

    [Test]
    public void DockerTop_ReportsResidentBytes_WithoutNeedingContainerExec()
    {
        var rows = WorkloadMemoryService.ParseDockerTop("abcdef012345", "PID RSS COMMAND\n10 2048 postgres\n20 1024 postgres worker\n");
        rows.Should().HaveCount(2);
        rows[0].Bytes.Should().Be(2097152L);
        rows[1].Name.Should().Be("postgres worker");
        rows.Should().OnlyContain(r => r.Metric == "Process RSS");
    }

    [Test]
    [Arguments("tcp://example.com:2375", false)]
    [Arguments("ssh://server", false)]
    [Arguments("npipe:////server/pipe/docker_engine", false)]
    [Arguments("npipe:////./pipe/docker_engine", true)]
    [Arguments("unix:///var/run/docker.sock", true)]
    public void DockerEndpoint_ExcludesRemoteMachines(string endpoint, bool expected)
        => WorkloadMemoryService.IsLocalEndpoint(endpoint).Should().Be(expected);
}
