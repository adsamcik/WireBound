using System.Collections.Concurrent;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace WireBound.Avalonia.Converters;

/// <summary>
/// Resolves a <c>WbIcon</c> <see cref="Controls.WbIcon.IconKey"/> (+ optional tint
/// brush) to the bitmap to render. Most icons render as their native generated
/// raster; a small set of semantic icons (download, upload, CPU, memory, status)
/// are recoloured to their theme brush so the meaning stays legible. An explicit
/// tint brush always wins over the per-key default.
///
/// <para>
/// Tinting preserves the icon's alpha silhouette (SrcIn blend), so the shape and
/// soft glow are kept while the colour is replaced. Tinted bitmaps are cached by
/// (key, colour).
/// </para>
/// </summary>
public sealed class TintedIconConverter : IMultiValueConverter
{
    public static readonly TintedIconConverter Instance = new();

    private static readonly ConcurrentDictionary<string, Bitmap?> _tintCache = new(StringComparer.Ordinal);

    /// <summary>
    /// Canonical semantic colour per icon key. Icons not listed render natively.
    /// </summary>
    private static readonly Dictionary<string, string> _defaultTintBrush = new(StringComparer.Ordinal)
    {
        ["WbMetricDownload"] = "DownloadBrush",
        ["WbMetricUpload"] = "UploadBrush",
        ["WbEntityCpu"] = "CpuBrush",
        ["WbEntityMemory"] = "MemoryBrush",
        ["WbStatusWarning"] = "WarningBrush",
        ["WbStatusError"] = "ErrorBrush",
        ["WbStatusSuccess"] = "SuccessBrush",
    };

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = values.Count > 0 ? values[0] as string : null;
        if (string.IsNullOrEmpty(key))
            return null;

        var tint = ResolveTintColor(values.Count > 1 ? values[1] : null, key);
        if (tint is null)
        {
            // No tint — render the native generated raster.
            return IconKeyToBitmapConverter.Instance.Convert(key, typeof(Bitmap), null, culture);
        }

        return _tintCache.GetOrAdd($"{key}|{tint.Value}", _ => LoadTinted(key, tint.Value));
    }

    /// <summary>
    /// Explicit tint brush wins; otherwise the per-key default brush (resolved from
    /// the app theme); otherwise null (render native).
    /// </summary>
    private static Color? ResolveTintColor(object? explicitTint, string key)
    {
        if (explicitTint is ISolidColorBrush explicitBrush)
            return explicitBrush.Color;

        if (_defaultTintBrush.TryGetValue(key, out var brushKey)
            && Application.Current is { } app
            && app.Resources.TryGetResource(brushKey, app.ActualThemeVariant, out var res)
            && res is ISolidColorBrush themedBrush)
        {
            return themedBrush.Color;
        }

        return null;
    }

    private static Bitmap? LoadTinted(string key, Color color)
    {
        var fileName = IconKeyToBitmapConverter.ToHyphenatedFileName(key);
        var uri = new Uri($"avares://WireBound/Assets/Icons/{fileName}.png");
        return IconRasterFactory.Load(uri, new SKColor(color.R, color.G, color.B, color.A));
    }
}
