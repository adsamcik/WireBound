using CommunityToolkit.Mvvm.ComponentModel;

namespace WireBound.Avalonia.ViewModels;

/// <summary>
/// Display model for secondary adapters with active traffic shown on the dashboard.
/// These overlay the primary adapter's data with additional context.
/// </summary>
public sealed partial class SecondaryAdapterInfo : ObservableObject
{
    public required string AdapterId { get; init; }
    public required string Name { get; init; }
    public required string Icon { get; init; }

    [ObservableProperty]
    private string _downloadSpeed = "";

    [ObservableProperty]
    private string _uploadSpeed = "";

    [ObservableProperty]
    private long _downloadBps;

    [ObservableProperty]
    private long _uploadBps;

    public bool IsVpn { get; init; }
    public required string ColorHex { get; init; }
}
