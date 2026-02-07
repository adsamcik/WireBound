using MessagePack;

namespace WireBound.IPC.Messages;

[MessagePackObject]
public class ProcessStatsRequest
{
    [Key(0)]
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// Optional filter: only return stats for these PIDs.
    /// Empty means return all.
    /// </summary>
    [Key(1)]
    public List<int> ProcessIds { get; set; } = [];
}
