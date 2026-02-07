namespace WireBound.IPC.Messages;

/// <summary>
/// Base envelope for all IPC messages.
/// </summary>
public class IpcMessage
{
    public string Type { get; set; } = string.Empty;
    public string RequestId { get; set; } = Guid.NewGuid().ToString("N");
    public string Payload { get; set; } = string.Empty;
}
