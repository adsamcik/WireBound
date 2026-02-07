namespace WireBound.IPC.Messages;

public class AuthenticateRequest
{
    public int ClientPid { get; set; }
    public long Timestamp { get; set; }
    public string Signature { get; set; } = string.Empty;
}
