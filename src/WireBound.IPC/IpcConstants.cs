namespace WireBound.IPC;

public static class IpcConstants
{
    // Windows named pipe
    public const string WindowsPipeName = "WireBound.Helper";

    // Linux Unix socket
    public const string LinuxSocketPath = "/run/wirebound/helper.sock";

    // Message types
    public const string AuthenticateType = "authenticate";
    public const string ConnectionStatsType = "connection_stats";
    public const string HeartbeatType = "heartbeat";
    public const string ShutdownType = "shutdown";

    // Session limits
    public static readonly TimeSpan MaxSessionDuration = TimeSpan.FromHours(8);
    public const int MaxConcurrentSessions = 10;
    public const int MaxRequestsPerSecond = 100;
}
