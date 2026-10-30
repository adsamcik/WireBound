namespace WireBound.Platform.Abstract.Models;

/// <summary>A workload observation. Its metric is deliberately separate from host physical ownership.</summary>
public sealed record MemoryWorkload(
    string Id, string Name, long? Bytes, string Metric, string Detail,
    IReadOnlyList<MemoryWorkload>? Children = null, string? ContainerId = null, string Summary = "");
