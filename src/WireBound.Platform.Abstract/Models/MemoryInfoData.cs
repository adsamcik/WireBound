namespace WireBound.Platform.Abstract.Models;

/// <summary>
/// Memory information data transfer object from platform providers
/// </summary>
public sealed class MemoryInfoData
{
    /// <summary>
    /// Total physical memory in bytes
    /// </summary>
    public long TotalBytes { get; init; }

    /// <summary>
    /// Used physical memory in bytes
    /// </summary>
    public long UsedBytes { get; init; }

    /// <summary>
    /// Available physical memory in bytes
    /// </summary>
    public long AvailableBytes { get; init; }

    /// <summary>
    /// Total virtual memory (page file + physical) in bytes
    /// </summary>
    public long TotalVirtualBytes { get; init; }

    /// <summary>
    /// Used virtual memory in bytes
    /// </summary>
    public long UsedVirtualBytes { get; init; }
}
