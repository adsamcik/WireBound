namespace WireBound.Platform.Abstract.Models;

/// <summary>Services observed in one svchost process. Memory belongs to the host, not to each service.</summary>
public sealed record ServiceHostInfo(IReadOnlyList<HostedServiceInfo> Services);

public sealed record HostedServiceInfo(string Name, string DisplayName);
