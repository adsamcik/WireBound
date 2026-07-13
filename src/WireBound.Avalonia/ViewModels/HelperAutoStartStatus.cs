namespace WireBound.Avalonia.ViewModels;

/// <summary>
/// Three-state status of the elevation helper auto-start chain shown in
/// Settings so the user can distinguish "I enabled it" from "it actually
/// works" without reading logs.
/// </summary>
public enum HelperAutoStartStatus
{
    /// <summary>
    /// Auto-start is disabled (user has not opted in).
    /// </summary>
    Disabled,

    /// <summary>
    /// Auto-start is registered with the OS (scheduled task / systemd unit
    /// installed) but the helper is not currently running.
    /// </summary>
    Registered,

    /// <summary>
    /// The helper process is running but the main app is not currently
    /// authenticated to it.
    /// </summary>
    Running,

    /// <summary>
    /// The helper is running and the main app is connected and authenticated.
    /// This is the happy path.
    /// </summary>
    Connected,

    /// <summary>
    /// The registered scheduled task / systemd unit has been mutated to point
    /// somewhere other than the expected helper binary. Refuse to auto-connect
    /// and surface a "Repair" affordance.
    /// </summary>
    Tampered,

    /// <summary>
    /// Auto-start is not supported on this platform.
    /// </summary>
    NotSupported
}
