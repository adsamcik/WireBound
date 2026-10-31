namespace WireBound.Platform.Abstract.Models;

/// <summary>Services observed in one process. Usage belongs to the process, not to each service.</summary>
public sealed record ServiceHostInfo(IReadOnlyList<HostedServiceInfo> Services, bool IsSharedHost = true);

public sealed record HostedServiceInfo(string Name, string DisplayName);
