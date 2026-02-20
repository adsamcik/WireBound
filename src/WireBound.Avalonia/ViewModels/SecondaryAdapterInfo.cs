namespace WireBound.Avalonia.ViewModels;

/// <summary>
/// Display model for secondary adapters with active traffic shown on the dashboard.
/// These overlay the primary adapter's data with additional context.
/// </summary>
public sealed class SecondaryAdapterInfo
{
    public required string AdapterId { get; init; }
    public required string Name { get; init; }
    public required string Icon { get; init; }
    public required string DownloadSpeed { get; set; }
    public required string UploadSpeed { get; set; }
    public long DownloadBps { get; set; }
    public long UploadBps { get; set; }
    public bool IsVpn { get; init; }
    public required string ColorHex { get; init; }
}
