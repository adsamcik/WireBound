using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Serilog;

namespace WireBound.Avalonia.Converters;

/// <summary>
/// Converts a local PNG file path into an Avalonia <see cref="Bitmap"/> for
/// binding to <c>Image.Source</c>. Returns <c>null</c> when the path is null,
/// empty, missing on disk, or fails to decode — the UI is expected to render
/// a placeholder behind the image in that case.
/// </summary>
/// <remarks>
/// <para>
/// Bitmaps are cached by path so repeated bindings to the same file (the
/// common case when sorting/filtering the same list) don't re-decode the
/// PNG every render. The cache is keyed by the absolute path and is
/// process-wide; entries are not evicted because the count is bounded by
/// the number of distinct AppIdentifiers seen, which is ~hundreds at most.
/// </para>
/// <para>
/// All errors are swallowed and logged at Debug — icon decoding is best-effort
/// UI polish. We never bubble exceptions out of a binding converter; that
/// would crash the entire ListBox render pass.
/// </para>
/// </remarks>
public sealed class IconPathToBitmapConverter : IValueConverter
{
    public static readonly IconPathToBitmapConverter Instance = new();

    private static readonly ConcurrentDictionary<string, Bitmap?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path))
        {
            return null;
        }

        return Cache.GetOrAdd(path, LoadBitmap);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => global::Avalonia.Data.BindingOperations.DoNothing;

    private static Bitmap? LoadBitmap(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            // Open via FileStream so we don't keep a lock on the cache PNG
            // after the Bitmap is loaded — the underlying decoder buffers
            // the entire image into memory by the time the constructor returns.
            using var stream = File.OpenRead(path);
            return new Bitmap(stream);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to load app icon from {Path}", path);
            return null;
        }
    }
}
