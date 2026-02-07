namespace WireBound.IPC;

public static class IpcConstants
{
    // Windows named pipe
    public const string WindowsPipeName = "WireBound.Elevation";

    // Linux Unix socket
    public const string LinuxSocketPath = "/run/wirebound/elevation.sock";

    // Session limits
    public static readonly TimeSpan MaxSessionDuration = TimeSpan.FromHours(8);
    public const int MaxConcurrentSessions = 10;
    public const int MaxRequestsPerSecond = 100;

    // Transport limits
    public const int MaxMessageSize = 1_048_576; // 1 MB

    // Auth
    public const int TimestampFreshnessSeconds = 30;
    public const int MaxAuthAttemptsPerSecond = 5;
    public const int MaxConsecutiveAuthFailures = 5;
}
