using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Windows.Services;

/// <summary>
/// Extracts the small executable icon associated with a Windows binary and
/// caches the result as a PNG under <c>%LocalAppData%/WireBound/app-icons/</c>.
/// Each <c>AppIdentifier</c> is extracted at most once per session; the cached
/// file persists across runs so subsequent loads of the Apps tab are immediate.
/// </summary>
/// <remarks>
/// <para>
/// Uses <see cref="Icon.ExtractAssociatedIcon(string)"/> which is the
/// recommended P/Invoke-free entry point for "give me whatever icon Explorer
/// would show for this file". The returned icon is converted to a 32×32
/// <see cref="Bitmap"/> and saved as PNG so Avalonia can render it via a
/// plain <c>Image</c> binding without an icon-specific decoder.
/// </para>
/// <para>
/// Failures (missing file, access denied, icon-less binary) return <c>null</c>
/// silently — the caller falls back to the generic placeholder. We never
/// throw across the public boundary because per-app icon extraction is
/// best-effort UI polish, not core functionality.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsAppIconService : IAppIconService
{
    private const int IconPixelSize = 32;

    private readonly ILogger<WindowsAppIconService>? _logger;
    private readonly string _cacheDirectory;
    private readonly ConcurrentDictionary<string, Task<string?>> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string?> _resolved = new(StringComparer.OrdinalIgnoreCase);

    public WindowsAppIconService(ILogger<WindowsAppIconService>? logger = null)
    {
        _logger = logger;
        _cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WireBound",
            "app-icons");
    }

    public Task<string?> GetIconPathAsync(
        string executablePath,
        string appIdentifier,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || string.IsNullOrWhiteSpace(appIdentifier))
        {
            return Task.FromResult<string?>(null);
        }

        // Fast path: already resolved (success OR known-miss) for this app id.
        if (_resolved.TryGetValue(appIdentifier, out var cached))
        {
            return Task.FromResult(cached);
        }

        // Coalesce concurrent callers asking for the same id: only one extracts.
        var task = _inFlight.GetOrAdd(appIdentifier, _ => ExtractAndCacheAsync(executablePath, appIdentifier, ct));
        return task;
    }

    private async Task<string?> ExtractAndCacheAsync(
        string executablePath,
        string appIdentifier,
        CancellationToken ct)
    {
        try
        {
            // Use the AppIdentifier as the cache filename so the on-disk cache
            // survives app upgrades that move the binary path. Folder is created
            // lazily on first use; cheap to call repeatedly.
            Directory.CreateDirectory(_cacheDirectory);
            var iconPath = Path.Combine(_cacheDirectory, $"{appIdentifier}.png");

            if (File.Exists(iconPath))
            {
                _resolved[appIdentifier] = iconPath;
                return iconPath;
            }

            if (!File.Exists(executablePath))
            {
                _resolved[appIdentifier] = null;
                return null;
            }

            // ExtractAssociatedIcon is a synchronous shell call; do it on a
            // worker thread so UI/poll threads aren't blocked by disk I/O.
            await Task.Run(() => ExtractToPng(executablePath, iconPath), ct).ConfigureAwait(false);

            var success = File.Exists(iconPath);
            _resolved[appIdentifier] = success ? iconPath : null;
            return success ? iconPath : null;
        }
        catch (OperationCanceledException)
        {
            // Don't cache a cancellation — let the next caller try again.
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to extract icon for {AppIdentifier} at {Path}", appIdentifier, executablePath);
            _resolved[appIdentifier] = null;
            return null;
        }
        finally
        {
            // Allow re-entry by other callers once we have a definitive answer
            // (cached in _resolved). Future GetIconPathAsync hits the fast path.
            _inFlight.TryRemove(appIdentifier, out _);
        }
    }

    private static void ExtractToPng(string executablePath, string destinationPath)
    {
        using var icon = Icon.ExtractAssociatedIcon(executablePath);
        if (icon is null)
        {
            return;
        }

        // Convert the raw icon to a 32×32 bitmap so we get a single consistent
        // size regardless of the icon resource the binary exposes. Save as PNG
        // for lossless transparency support.
        using var bitmap = new Bitmap(IconPixelSize, IconPixelSize);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            graphics.DrawIcon(icon, new Rectangle(0, 0, IconPixelSize, IconPixelSize));
        }

        // Write atomically: render to a temp file, then move into place. Avoids
        // a half-written PNG on the next read if the process is killed mid-save.
        var tempPath = destinationPath + ".tmp";
        bitmap.Save(tempPath, ImageFormat.Png);
        File.Move(tempPath, destinationPath, overwrite: true);
    }
}
