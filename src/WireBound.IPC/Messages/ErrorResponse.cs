using MessagePack;

namespace WireBound.IPC.Messages;

[MessagePackObject]
public class ErrorResponse
{
    [Key(0)]
    public string Error { get; set; } = string.Empty;

    [Key(1)]
    public string? Details { get; set; }
}
