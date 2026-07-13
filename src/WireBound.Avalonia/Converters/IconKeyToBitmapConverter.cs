using System.Collections.Concurrent;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace WireBound.Avalonia.Converters;

/// <summary>
/// Converts a string icon key (e.g. <c>"WbNavOverview"</c>) into the
/// matching <see cref="Bitmap"/> from the bundled raster icon set in
/// <c>Assets/Icons/</c>. The naming convention is documented in
/// <c>docs/ICON_SYSTEM_REDESIGN.md</c>: PascalCase keys map to
/// <c>wb-</c>-prefixed hyphenated filenames.
///
/// <para>
/// Source rasters are transparent PNGs generated or curated for the icon
/// set with enough safety margin to downscale cleanly at the 16-64 px sizes
/// the UI actually renders.
/// </para>
///
/// <para>
/// Successfully resolved bitmaps are cached so the same icon doesn't get
/// re-decoded on every binding refresh. Missing keys return <c>null</c> and
/// the consuming <see cref="Controls.WbIcon"/> simply renders nothing —
/// preferable to crashing the layout for an icon-set typo.
/// </para>
/// </summary>
public sealed class IconKeyToBitmapConverter : IValueConverter
{
    public static readonly IconKeyToBitmapConverter Instance = new();

    private static readonly ConcurrentDictionary<string, Bitmap?> _cache = new(StringComparer.Ordinal);

    /// <summary>
    /// IconKey → file-stem aliases for keys whose semantic names don't map
    /// exactly to the default PascalCase-to-kebab-case convention.
    /// </summary>
    private static readonly Dictionary<string, string> _aliases = new(StringComparer.Ordinal)
    {
        ["WbAdapterWifi1"] = "wb-adapter-wifi-1",
        ["WbAdapterWifi2"] = "wb-adapter-wifi-2",
        ["WbAdapterWifi3"] = "wb-adapter-wifi-3",
        ["WbAdapterWifi4"] = "wb-adapter-wifi",
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || string.IsNullOrEmpty(key))
        {
            return parameter switch
            {
                "exists" => false,
                "missing" => true,
                _ => null
            };
        }

        var bitmap = _cache.GetOrAdd(key, LoadBitmap);

        return parameter switch
        {
            // IsVisible flags so the template can show/hide Image vs. Path
            // fallback without needing a second converter call site.
            "exists" => bitmap is not null,
            "missing" => bitmap is null,
            _ => (object?)bitmap
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    /// <summary>
    /// Maps <c>WbNavOverview</c> → <c>wb-nav-overview</c> and loads the
    /// matching PNG from the app's resource bundle. The transformation
    /// is: split CamelCase at each uppercase letter, lower-case, join
    /// with hyphens.
    /// </summary>
    private static Bitmap? LoadBitmap(string key)
    {
        var fileName = _aliases.TryGetValue(key, out var alias)
            ? alias
            : ToHyphenatedFileName(key);
        var uri = new Uri($"avares://WireBound/Assets/Icons/{fileName}.png");
        return IconRasterFactory.Load(uri, tint: null);
    }

    internal static string ToHyphenatedFileName(string key)
    {
        // Hand-rolled splitter — keeps the dependency surface small and
        // avoids the Regex JIT cost for what is effectively a startup-time
        // O(distinct keys) call.
        var sb = new System.Text.StringBuilder(key.Length + 4);
        for (var i = 0; i < key.Length; i++)
        {
            var c = key[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(key[i - 1]))
            {
                sb.Append('-');
            }
            sb.Append(char.ToLowerInvariant(c));
        }
        // Strip trailing digits so WifiX → wifi for the alias-free SVG path
        // lookup. Bitmap converter has its own alias map; SVGs use the bare
        // name (we only have one wb-adapter-wifi.svg, not 4 variants yet).
        while (sb.Length > 0 && char.IsDigit(sb[sb.Length - 1]))
        {
            sb.Length--;
        }
        return sb.ToString();
    }
}
