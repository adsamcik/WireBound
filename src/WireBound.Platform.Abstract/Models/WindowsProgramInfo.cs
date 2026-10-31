namespace WireBound.Platform.Abstract.Models;

/// <summary>Presentation metadata for a Windows executable; a null system role means it is not classified.</summary>
public sealed record WindowsProgramInfo(string DisplayName, bool? IsSystemProcess, bool SeparateInstances = false);
