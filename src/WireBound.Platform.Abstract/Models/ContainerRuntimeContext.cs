namespace WireBound.Platform.Abstract.Models;

public enum ContainerRuntime { Docker, RancherMoby, RancherContainerd }

/// <summary>Keep lazy container queries tied to the engine and namespace that supplied the sample.</summary>
public sealed record ContainerRuntimeContext(ContainerRuntime Runtime, string? Endpoint = null, string? Namespace = null);
