using System.Globalization;
using System.Text.Json;
using WireBound.Core.Services;
using WireBound.Platform.Abstract.Helpers;
using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Avalonia.Services;

/// <summary>Read local Docker and guest observations only when Memory requests a sample.</summary>
public sealed class WorkloadMemoryService(IWorkloadHostProvider hosts) : IWorkloadMemoryService
{
    public async Task<IReadOnlyList<MemoryWorkload>> CaptureAsync(CancellationToken cancellationToken)
    {
        var guests = hosts.GetVirtualMachinesAsync(cancellationToken);
        var docker = CaptureDockerAsync(cancellationToken);
        await Task.WhenAll(guests, docker).ConfigureAwait(false);
        return [.. await guests, await docker];
    }

    private static async Task<MemoryWorkload> CaptureDockerAsync(CancellationToken cancellationToken)
    {
        var endpoint = await GetLocalDockerEndpointAsync(cancellationToken).ConfigureAwait(false);
        if (endpoint is null)
            return new("docker", "Docker", null, "Unavailable",
                "Start your local Docker engine and refresh. Only local socket contexts are read; remote engines are excluded.");
        var text = await LocalCommandRunner.RunAsync("docker", ["--host", endpoint, "stats", "--no-stream", "--no-trunc", "--format", "{{json .}}"], cancellationToken, 8);
        if (text is null)
            return new("docker", "Docker", null, "Unavailable", "Docker memory is unavailable. Check the local engine and socket permissions, then refresh.");
        try
        {
            var containers = ParseDockerStats(text);
            return new("docker", "Docker", containers.Count > 0 && containers.All(c => c.Bytes.HasValue)
                ? containers.Sum(c => c.Bytes!.Value) : null,
                "Container usage", containers.Count == 0 ? "No running containers in the local Docker context."
                    : "Docker stats · container usage can overlap VM and host observations. Linux cache is excluded. Expand a container for processes.", containers);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or OverflowException)
        {
            return new("docker", "Docker", null, "Unavailable", "Docker returned an unreadable memory sample. Refresh to retry.");
        }
    }

    private static async Task<string?> GetLocalDockerEndpointAsync(CancellationToken cancellationToken)
    {
        var endpoint = Environment.GetEnvironmentVariable("DOCKER_HOST");
        if (string.IsNullOrWhiteSpace(endpoint) || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOCKER_CONTEXT")))
            endpoint = await LocalCommandRunner.RunAsync("docker", ["context", "inspect", "--format", "{{.Endpoints.docker.Host}}"], cancellationToken);
        return IsLocalEndpoint(endpoint?.Trim()) ? endpoint!.Trim() : null;
    }

    internal static bool IsLocalEndpoint(string? endpoint) => endpoint is not null
        && (endpoint.StartsWith("unix:///", StringComparison.OrdinalIgnoreCase)
            || endpoint.StartsWith("npipe:////./pipe/", StringComparison.OrdinalIgnoreCase));

    public async Task<IReadOnlyList<MemoryWorkload>> GetContainerProcessesAsync(string containerId, CancellationToken cancellationToken)
    {
        var endpoint = await GetLocalDockerEndpointAsync(cancellationToken).ConfigureAwait(false);
        if (containerId.Length is < 12 or > 64 || !containerId.All(Uri.IsHexDigit) || endpoint is null)
            return [new("docker-process-status:" + containerId, "Processes unavailable", null, "Unavailable", "The local Docker context is unavailable. Refresh and try again.")];
        var text = await LocalCommandRunner.RunAsync("docker", ["--host", endpoint, "top", containerId, "-eo", "pid,rss,comm"], cancellationToken);
        return text is null ? [new("docker-process-status:" + containerId, "Processes unavailable", null, "Unavailable",
            "This engine cannot report process RSS, or the container stopped. Container totals remain available; no command is executed inside the container.")]
            : ParseDockerTop(containerId, text);
    }

    internal static IReadOnlyList<MemoryWorkload> ParseDockerStats(string text)
    {
        var rows = new List<MemoryWorkload>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;
            var id = root.GetProperty("ID").GetString() ?? "";
            var name = root.GetProperty("Name").GetString() ?? id;
            var memory = root.GetProperty("MemUsage").GetString() ?? "";
            rows.Add(new("docker:" + id, name, ParseSize(memory.Split('/')[0].Trim()), "Container usage",
                $"Docker stats · {memory}. Processes report RSS, which may share pages and does not sum to container usage.", ContainerId: id));
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
    {
        var rows = new List<MemoryWorkload>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1))
        {
            var parts = line.Split((char[]?)null, 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3 || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var kib)
                || kib > long.MaxValue / 1024) continue;
            rows.Add(new($"docker-process:{containerId}:{parts[0]}", parts[2].Trim(), kib * 1024, "Process RSS",
                $"Docker top · daemon PID {parts[0]}. Resident memory includes shared pages; outside host physical totals.", Summary: $"PID {parts[0]}"));
        }
        return rows.Count > 0 ? rows.OrderByDescending(r => r.Bytes).ToArray()
            : [new("docker-process-status:" + containerId, "No process memory reported", null, "Unavailable", "The engine did not return process RSS. Refresh to retry.")];
    }
}
