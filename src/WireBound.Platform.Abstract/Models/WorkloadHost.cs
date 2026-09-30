namespace WireBound.Platform.Abstract.Models;

/// <summary>A recognized host process. This identifies the host, not guest-owned RAM.</summary>
public sealed record WorkloadHost(string Kind, string Label, string Evidence);
