namespace WireBound.Platform.Abstract.Models;

/// <summary>An independently measured resident system allocation, excluding process-private pages.</summary>
public sealed record ResidentMemoryAllocation(string Id, string Name, long Bytes, string Metric, string Detail);
