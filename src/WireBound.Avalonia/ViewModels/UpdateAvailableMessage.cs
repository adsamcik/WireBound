namespace WireBound.Avalonia.ViewModels;

/// <summary>
/// Message sent when an update is available, so MainViewModel can show a badge.
/// </summary>
public record UpdateAvailableMessage(string Version);
