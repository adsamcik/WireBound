using System.Globalization;
using System.Text.Json;
using WireBound.Core.Services;
using WireBound.Platform.Abstract.Helpers;
using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Avalonia.Services;

/// <summary>Read local container engines and guest observations only when Memory requests a sample.</summary>
public sealed class WorkloadMemoryService : IWorkloadMemoryService
{
    private const string RancherSocket = "/run/k3s/containerd/containerd.sock";
    private readonly IWorkloadHostProvider _hosts;
    private readonly Func<WorkloadCommand, CancellationToken, Task<string?>> _run;
    private readonly Func<RancherToolPaths?> _findRancher;
    private readonly Func<string, string?> _environment;
    private static readonly IReadOnlyDictionary<string, string?> DockerEnvironment = new Dictionary<string, string?>
    {
        ["DOCKER_HOST"] = null,
        ["DOCKER_CONTEXT"] = null
    };
    private static readonly IReadOnlyDictionary<string, string?> RancherEnvironment = new Dictionary<string, string?>
    {
        ["RD_WSL_DISTRO"] = "rancher-desktop",
        ["RD_NERDCTL"] = "/usr/local/bin/nerdctl",
        ["CONTAINERD_ADDRESS"] = RancherSocket,
        ["CONTAINERD_NAMESPACE"] = null
    };

    public WorkloadMemoryService(IWorkloadHostProvider hosts) : this(hosts,
        (command, token) => LocalCommandRunner.RunAsync(command.Executable, command.Arguments, token,
            command.TimeoutSeconds, environment: command.Environment), RancherToolPaths.Find)
    { }

    internal WorkloadMemoryService(IWorkloadHostProvider hosts,
        Func<WorkloadCommand, CancellationToken, Task<string?>> run, Func<RancherToolPaths?> findRancher,
        Func<string, string?>? environment = null)
    {
        _hosts = hosts;
        _run = run;
        _findRancher = findRancher;
        _environment = environment ?? Environment.GetEnvironmentVariable;
    }

    public async Task<IReadOnlyList<MemoryWorkload>> CaptureAsync(CancellationToken cancellationToken)
    {
        var guests = _hosts.GetVirtualMachinesAsync(cancellationToken);
        var endpointTask = GetLocalDockerEndpointAsync(cancellationToken);
        var tools = _findRancher();
        var rancherTask = tools is null ? Task.FromResult<ContainerRuntime?>(null) : GetRancherRuntimeAsync(tools, cancellationToken);
        await Task.WhenAll(endpointTask, rancherTask).ConfigureAwait(false);
        var endpoint = await endpointTask;
        var runtime = await rancherTask;
        IReadOnlyList<MemoryWorkload> containers;
        if (runtime == ContainerRuntime.RancherMoby)
            containers = await CaptureMobyAndDockerAsync(tools!, endpoint, cancellationToken).ConfigureAwait(false);
        else
        {
            var docker = CaptureDockerAsync("docker", endpoint, new(ContainerRuntime.Docker, endpoint), cancellationToken);
            var rancher = runtime == ContainerRuntime.RancherContainerd
                ? CaptureContainerdAsync(tools!, cancellationToken) : Task.FromResult<IReadOnlyList<MemoryWorkload>>([]);
            await Task.WhenAll(docker, rancher).ConfigureAwait(false);
            containers = [.. await docker, .. await rancher];
        }
        return [.. await guests, .. containers];
    }

    private async Task<IReadOnlyList<MemoryWorkload>> CaptureDockerAsync(string executable, string? endpoint,
        ContainerRuntimeContext context, CancellationToken cancellationToken)
    {
        if (!IsLocalEndpoint(endpoint)) return [];
        var text = await _run(new(executable, ["--host", endpoint!, "stats", "--no-stream", "--no-trunc", "--format", "{{json .}}"],
            8, DockerEnvironment), cancellationToken).ConfigureAwait(false);
        return ParseStatsOrEmpty(text, context);
    }

    private async Task<string?> GetLocalDockerEndpointAsync(CancellationToken cancellationToken)
    {
        var endpoint = _environment("DOCKER_HOST");
        if (string.IsNullOrWhiteSpace(endpoint) || !string.IsNullOrWhiteSpace(_environment("DOCKER_CONTEXT")))
            endpoint = await _run(new("docker", ["context", "inspect", "--format", "{{.Endpoints.docker.Host}}"]), cancellationToken).ConfigureAwait(false);
        return IsLocalEndpoint(endpoint?.Trim()) ? endpoint!.Trim() : null;
    }

    internal static bool IsLocalEndpoint(string? endpoint) => endpoint is not null
        && (endpoint.StartsWith("unix:///", StringComparison.OrdinalIgnoreCase)
            || endpoint.StartsWith("npipe:////./pipe/", StringComparison.OrdinalIgnoreCase));

    private async Task<ContainerRuntime?> GetRancherRuntimeAsync(RancherToolPaths tools, CancellationToken cancellationToken)
    {
        var state = await _run(new(tools.Rdctl, ["--host", "127.0.0.1", "api", "/v1/backend_state"], 3), cancellationToken).ConfigureAwait(false);
        if (state is null) return null;
        try
        {
            using var backend = JsonDocument.Parse(state);
            if (backend.RootElement.GetProperty("vmState").GetString() is not ("STARTED" or "DISABLED")
                || backend.RootElement.GetProperty("locked").ValueKind != JsonValueKind.False) return null;
            var text = await _run(new(tools.Rdctl, ["--host", "127.0.0.1", "list-settings"], 3), cancellationToken).ConfigureAwait(false);
            if (text is null) return null;
            using var settings = JsonDocument.Parse(text);
            return settings.RootElement.GetProperty("containerEngine").GetProperty("name").GetString() switch
            {
                "containerd" => ContainerRuntime.RancherContainerd,
                "moby" or "docker" => ContainerRuntime.RancherMoby,
                _ => null
            };
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }

    private async Task<IReadOnlyList<MemoryWorkload>> CaptureContainerdAsync(RancherToolPaths tools, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(12));
        var text = await _run(new(tools.Nerdctl, ["--address", RancherSocket, "namespace", "ls", "--quiet"],
            3, RancherEnvironment), deadline.Token).ConfigureAwait(false);
        if (text is null) return [];
        var namespaces = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(IsNamespace).Distinct(StringComparer.Ordinal).ToArray();
        using var gate = new SemaphoreSlim(4);
        var readings = await Task.WhenAll(namespaces.Select(async ns =>
        {
            try
            {
                await gate.WaitAsync(deadline.Token).ConfigureAwait(false);
                try
                {
                    var stats = await _run(new(tools.Nerdctl,
                        ["--address", RancherSocket, "--namespace", ns, "stats", "--no-stream", "--no-trunc", "--format", "{{json .}}"],
                        8, RancherEnvironment), deadline.Token).ConfigureAwait(false);
                    return ParseStatsOrEmpty(stats, new(ContainerRuntime.RancherContainerd, RancherSocket, ns));
                }
                finally { gate.Release(); }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return []; }
        })).ConfigureAwait(false);
        return readings.SelectMany(r => r).ToArray();
    }

    private async Task<IReadOnlyList<MemoryWorkload>> CaptureMobyAndDockerAsync(RancherToolPaths tools, string? endpoint, CancellationToken cancellationToken)
    {
        var rancherEndpoint = (await _run(new(tools.Docker,
            ["context", "inspect", "rancher-desktop", "--format", "{{.Endpoints.docker.Host}}"], Environment: DockerEnvironment), cancellationToken).ConfigureAwait(false))?.Trim();
        var explicitContext = IsLocalEndpoint(rancherEndpoint);
        if (!explicitContext) rancherEndpoint = OperatingSystem.IsWindows() ? "npipe:////./pipe/docker_engine" : null;
        var info = IsLocalEndpoint(rancherEndpoint) ? await GetDockerInfoAsync(tools.Docker, rancherEndpoint!, cancellationToken).ConfigureAwait(false) : null;
        if (info is null || !explicitContext && !info.Value.Name.Contains("rancher-desktop", StringComparison.OrdinalIgnoreCase))
            return await CaptureDockerAsync("docker", endpoint, new(ContainerRuntime.Docker, endpoint), cancellationToken).ConfigureAwait(false);
        var rancher = CaptureDockerAsync(tools.Docker, rancherEndpoint, new(ContainerRuntime.RancherMoby, rancherEndpoint), cancellationToken);
        var sameEndpoint = string.Equals(endpoint, rancherEndpoint,
            rancherEndpoint!.StartsWith("npipe:", StringComparison.OrdinalIgnoreCase) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        var dockerInfo = !sameEndpoint && endpoint is not null ? await GetDockerInfoAsync("docker", endpoint, cancellationToken).ConfigureAwait(false) : null;
        if (sameEndpoint || dockerInfo?.Id == info.Value.Id) return await rancher.ConfigureAwait(false);
        var docker = await CaptureDockerAsync("docker", endpoint, new(ContainerRuntime.Docker, endpoint), cancellationToken).ConfigureAwait(false);
        return [.. docker, .. await rancher];
    }

    private async Task<(string Id, string Name)?> GetDockerInfoAsync(string executable, string endpoint, CancellationToken cancellationToken)
    {
        var text = await _run(new(executable, ["--host", endpoint, "info", "--format", "{\"ID\":{{json .ID}},\"Name\":{{json .Name}}}"],
            Environment: DockerEnvironment), cancellationToken).ConfigureAwait(false);
        if (text is null) return null;
        try
        {
            using var info = JsonDocument.Parse(text);
            var id = info.RootElement.GetProperty("ID").GetString();
            return string.IsNullOrWhiteSpace(id) ? null : (id, info.RootElement.GetProperty("Name").GetString() ?? "");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }

    internal static bool IsNamespace(string value) => value.Length is > 0 and <= 128 && char.IsAsciiLetterOrDigit(value[0])
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

    private static bool IsContainerId(string value, ContainerRuntimeContext? context) => context?.Runtime == ContainerRuntime.RancherContainerd
        ? IsNamespace(value) : value.Length is >= 12 and <= 64 && value.All(Uri.IsHexDigit);

    public async Task<IReadOnlyList<MemoryWorkload>> GetContainerProcessesAsync(string containerId, CancellationToken cancellationToken,
        ContainerRuntimeContext? context = null)
    {
        IReadOnlyList<MemoryWorkload> Unavailable(string detail) => [new(ContainerPrefix(context) + "-process-status:" + containerId,
            "Processes unavailable", null, "Unavailable", detail)];
        if (!IsContainerId(containerId, context)) return Unavailable("Refresh the container sample and try again.");
        if (context is not null && context.Runtime is not (ContainerRuntime.Docker or ContainerRuntime.RancherMoby or ContainerRuntime.RancherContainerd))
            return Unavailable("Refresh the container sample and try again.");
        string? text;
        if (context?.Runtime is ContainerRuntime.RancherContainerd or ContainerRuntime.RancherMoby)
        {
            var tools = _findRancher();
            if (tools is null || await GetRancherRuntimeAsync(tools, cancellationToken).ConfigureAwait(false) != context.Runtime)
                return Unavailable("Rancher Desktop is unavailable or its engine changed. Refresh to retry.");
            if (context.Runtime == ContainerRuntime.RancherContainerd)
            {
                if (context.Namespace is null || !IsNamespace(context.Namespace) || context.Endpoint != RancherSocket)
                    return Unavailable("Refresh the container sample and try again.");
                text = await _run(new(tools.Nerdctl,
                    ["--address", RancherSocket, "--namespace", context.Namespace, "top", containerId, "-eo", "pid,rss,comm"],
                    Environment: RancherEnvironment), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                if (!IsLocalEndpoint(context.Endpoint)) return Unavailable("Only local container engines are read.");
                text = await _run(new(tools.Docker, ["--host", context.Endpoint!, "top", containerId, "-eo", "pid,rss,comm"],
                    Environment: DockerEnvironment), cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            var endpoint = context?.Endpoint ?? await GetLocalDockerEndpointAsync(cancellationToken).ConfigureAwait(false);
            if (!IsLocalEndpoint(endpoint)) return Unavailable("The local Docker context is unavailable. Refresh to retry.");
            text = await _run(new("docker", ["--host", endpoint!, "top", containerId, "-eo", "pid,rss,comm"],
                Environment: DockerEnvironment), cancellationToken).ConfigureAwait(false);
        }
        return text is null ? Unavailable("Process RSS could not be read, or the container stopped. Refresh to retry.")
            : ParseContainerTop(containerId, text, context);
    }

    private static IReadOnlyList<MemoryWorkload> ParseStatsOrEmpty(string? text, ContainerRuntimeContext context)
    {
        if (text is null) return [];
        try { return ParseContainerStats(text, context); }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or OverflowException) { return []; }
    }

    internal static IReadOnlyList<MemoryWorkload> ParseDockerStats(string text)
        => ParseContainerStats(text, new(ContainerRuntime.Docker));

    internal static IReadOnlyList<MemoryWorkload> ParseContainerStats(string text, ContainerRuntimeContext context)
    {
        var rows = new List<MemoryWorkload>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;
            var id = root.GetProperty("ID").GetString() ?? "";
            if (!IsContainerId(id, context)) throw new JsonException("Invalid container ID");
            var name = root.GetProperty("Name").GetString();
            var memory = root.GetProperty("MemUsage").GetString() ?? "";
            rows.Add(new(ContainerPrefix(context) + ":" + id, string.IsNullOrWhiteSpace(name) ? id : name,
                ParseSize(memory.Split('/')[0].Trim()), "Container usage",
                $"{ContainerSource(context)} stats · {memory}. Container usage can overlap VM and host observations. Process RSS may share pages and does not sum to container usage.",
                ContainerId: id, Summary: context.Namespace ?? "", ContainerContext: context));
        }
        return rows.OrderByDescending(c => c.Bytes).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static long? ParseSize(string text)
    {
        var index = 0;
        while (index < text.Length && (char.IsAsciiDigit(text[index]) || text[index] == '.')) index++;
        if (!decimal.TryParse(text[..index], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)) return null;
        var multiplier = text[index..].Trim() switch
        {
            "B" => 1m,
            "kB" or "KB" => 1000m,
            "MB" => 1_000_000m,
            "GB" => 1_000_000_000m,
            "KiB" => 1024m,
            "MiB" => 1_048_576m,
            "GiB" => 1_073_741_824m,
            "TiB" => 1_099_511_627_776m,
            _ => 0m
        };
        var bytes = value * multiplier;
        return multiplier > 0 && bytes <= long.MaxValue ? (long)bytes : null;
    }

    internal static IReadOnlyList<MemoryWorkload> ParseDockerTop(string containerId, string text)
        => ParseContainerTop(containerId, text, null);

    private static string ContainerPrefix(ContainerRuntimeContext? context) => context?.Runtime switch
    {
        ContainerRuntime.RancherContainerd => "rancher:containerd:" + context.Namespace,
        ContainerRuntime.RancherMoby => "rancher:moby",
        _ => "docker"
    };

    private static string ContainerSource(ContainerRuntimeContext? context) => context?.Runtime switch
    {
        ContainerRuntime.RancherContainerd => "Rancher Desktop · containerd · namespace " + context.Namespace,
        ContainerRuntime.RancherMoby => "Rancher Desktop · Moby",
        _ => "Docker"
    };

    private static IReadOnlyList<MemoryWorkload> ParseContainerTop(string containerId, string text, ContainerRuntimeContext? context)
    {
        var rows = new List<MemoryWorkload>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1))
        {
            var parts = line.Split((char[]?)null, 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3 || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var kib)
                || kib > long.MaxValue / 1024) continue;
            rows.Add(new($"{ContainerPrefix(context)}-process:{containerId}:{parts[0]}", parts[2].Trim(), kib * 1024, "Process RSS",
                $"{ContainerSource(context)} top · daemon PID {parts[0]}. Resident memory includes shared pages; outside host physical totals.",
                Summary: $"PID {parts[0]}"));
        }
        return rows.Count > 0 ? rows.OrderByDescending(r => r.Bytes).ToArray()
            : [new(ContainerPrefix(context) + "-process-status:" + containerId, "No process memory reported", null, "Unavailable", "The engine did not return process RSS. Refresh to retry.")];
    }
}
