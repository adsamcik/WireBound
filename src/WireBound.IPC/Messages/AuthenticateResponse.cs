namespace WireBound.IPC.Messages;

public class AuthenticateResponse
{
    public bool Success { get; set; }
    public string? SessionId { get; set; }
    public string? ErrorMessage { get; set; }
}
