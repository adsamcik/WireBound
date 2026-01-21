using System.ComponentModel.DataAnnotations;

namespace WireBound.Core.Models;

/// <summary>
/// Represents a single speed measurement snapshot for chart history
/// </summary>
public class SpeedSnapshot
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// The timestamp of this measurement
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Download speed in bytes per second
    /// </summary>
    public long DownloadSpeedBps { get; set; }

    /// <summary>
    /// Upload speed in bytes per second
    /// </summary>
    public long UploadSpeedBps { get; set; }
}
