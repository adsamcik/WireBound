namespace WireBound.Platform.Abstract.Services;

/// <summary>
/// Resolves a local cached PNG path for an application's icon so the UI
/// can render it via a standard image binding without expensive per-frame
/// extraction. Implementations cache extracted icons in a per-user folder
/// keyed by <c>AppIdentifier</c>; the same app on the same executable path
/// is extracted at most once per session.
/// </summary>
/// <remarks>
/// <para>
/// Returning <c>null</c> is the normal "no icon available" outcome — the
/// caller should fall back to a generic placeholder (e.g. an emoji) rather
/// than treating null as an error. Reasons it might be null: missing /
/// inaccessible executable, platform without an icon-extraction API (Linux
/// today), or extraction failure.
/// </para>
/// <para>
/// Returned paths are absolute file-system paths to PNG files written under
/// <c>%LocalAppData%/WireBound/app-icons/</c> (or the platform equivalent).
/// Callers may load them via any standard image API.
/// </para>
/// </remarks>
public interface IAppIconService
{
    /// <summary>
    /// Returns the local PNG path for the given app's icon, extracting it
    /// once if not already cached. Returns <c>null</c> when the icon cannot
    /// be obtained.
    /// </summary>
    Task<string?> GetIconPathAsync(
        string executablePath,
        string appIdentifier,
        CancellationToken ct = default);
}
