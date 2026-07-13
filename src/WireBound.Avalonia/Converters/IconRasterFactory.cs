using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace WireBound.Avalonia.Converters;

/// <summary>
/// Loads a generated icon raster and normalizes its padding so every icon renders
/// with consistent breathing room regardless of how tightly the source art was
/// cropped. The opaque content is trimmed to its bounding box and re-centered in a
/// square canvas where the content occupies <see cref="FillRatio"/> of the size.
/// Some generated source assets use a flat chroma-key background; those are
/// converted to alpha on load before trimming so they can be wired in directly.
/// Optionally recolours the icon (preserving its alpha shape) via an SrcIn blend.
/// Results are intended to be cached by the caller.
/// </summary>
internal static class IconRasterFactory
{
    private const int AlphaThreshold = 32;
    private const double FillRatio = 0.80; // content fills 80% -> ~10% margin each side
    private const byte ChromaKeyR = 0;
    private const byte ChromaKeyG = 255;
    private const byte ChromaKeyB = 0;
    private const int ChromaDetectTolerance = 8;
    private const int ChromaTransparentThreshold = 12;
    private const int ChromaOpaqueThreshold = 220;
    private const double ChromaBorderRatioThreshold = 0.75;

    public static Bitmap? Load(Uri uri, SKColor? tint)
    {
        try
        {
            if (!AssetLoader.Exists(uri))
                return null;

            using var stream = AssetLoader.Open(uri);
            using var decoded = SKBitmap.Decode(stream);
            if (decoded is null)
                return null;

            var src = decoded.ColorType == SKColorType.Rgba8888
                ? decoded
                : decoded.Copy(SKColorType.Rgba8888);
            try
            {
                ApplyChromaKeyIfNeeded(src);

                if (src is null || !TryGetAlphaBounds(src, out var bounds))
                    return null;

                int cw = bounds.Width;
                int ch = bounds.Height;
                int content = Math.Max(cw, ch);
                int canvas = Math.Max(1, (int)Math.Ceiling(content / FillRatio));

                var info = new SKImageInfo(canvas, canvas, SKColorType.Rgba8888, SKAlphaType.Premul);
                using var surface = SKSurface.Create(info);
                surface.Canvas.Clear(SKColors.Transparent);

                using var subset = new SKBitmap();
                src.ExtractSubset(subset, bounds);

                using var paint = new SKPaint { IsAntialias = true };
                if (tint is { } t)
                {
                    // Keep the alpha silhouette (shape + glow), replace the colour.
                    paint.ColorFilter = SKColorFilter.CreateBlendMode(t, SKBlendMode.SrcIn);
                }

                surface.Canvas.DrawBitmap(subset, (canvas - cw) / 2f, (canvas - ch) / 2f, paint);

                using var image = surface.Snapshot();
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                return new Bitmap(new MemoryStream(data.ToArray()));
            }
            finally
            {
                if (!ReferenceEquals(src, decoded))
                    src?.Dispose();
            }
        }
        catch
        {
            return null;
        }
    }

    private static bool TryGetAlphaBounds(SKBitmap bmp, out SKRectI bounds)
    {
        int w = bmp.Width;
        int h = bmp.Height;

        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (bmp.GetPixel(x, y).Alpha > AlphaThreshold)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }

        if (maxX < minX)
        {
            bounds = default;
            return false;
        }

        bounds = new SKRectI(minX, minY, maxX + 1, maxY + 1);
        return true;
    }

    private static void ApplyChromaKeyIfNeeded(SKBitmap? bmp)
    {
        if (bmp is null || !HasChromaKeyBorder(bmp))
            return;

        for (int y = 0; y < bmp.Height; y++)
        {
            for (int x = 0; x < bmp.Width; x++)
            {
                var pixel = bmp.GetPixel(x, y);
                byte srcR = pixel.Red;
                byte srcG = pixel.Green;
                byte srcB = pixel.Blue;
                byte srcA = pixel.Alpha;

                if (srcA == 0)
                    continue;

                int matte = Math.Max(Math.Abs(srcR - ChromaKeyR),
                    Math.Max(Math.Abs(srcG - ChromaKeyG), Math.Abs(srcB - ChromaKeyB)));

                byte outA = matte switch
                {
                    <= ChromaTransparentThreshold => 0,
                    >= ChromaOpaqueThreshold => 255,
                    _ => (byte)Math.Clamp(
                        (matte - ChromaTransparentThreshold) * 255 / (ChromaOpaqueThreshold - ChromaTransparentThreshold),
                        0,
                        255)
                };

                if (outA == 0)
                {
                    bmp.SetPixel(x, y, SKColors.Transparent);
                    continue;
                }

                float alpha = outA / 255f;
                bmp.SetPixel(x, y, new SKColor(
                    RecoverForegroundChannel(srcR, ChromaKeyR, alpha),
                    RecoverForegroundChannel(srcG, ChromaKeyG, alpha),
                    RecoverForegroundChannel(srcB, ChromaKeyB, alpha),
                    outA));
            }
        }
    }

    private static bool HasChromaKeyBorder(SKBitmap bmp)
    {
        int borderPixels = 0;
        int chromaPixels = 0;

        for (int y = 0; y < bmp.Height; y++)
        {
            for (int x = 0; x < bmp.Width; x++)
            {
                if (x != 0 && y != 0 && x != bmp.Width - 1 && y != bmp.Height - 1)
                    continue;

                borderPixels++;
                var pixel = bmp.GetPixel(x, y);
                if (pixel.Alpha == 0)
                    continue;

                if (Math.Abs(pixel.Red - ChromaKeyR) <= ChromaDetectTolerance &&
                    Math.Abs(pixel.Green - ChromaKeyG) <= ChromaDetectTolerance &&
                    Math.Abs(pixel.Blue - ChromaKeyB) <= ChromaDetectTolerance)
                {
                    chromaPixels++;
                }
            }
        }

        return borderPixels > 0 && (double)chromaPixels / borderPixels >= ChromaBorderRatioThreshold;
    }

    private static byte RecoverForegroundChannel(byte observed, byte background, float alpha)
    {
        if (alpha >= 0.999f)
            return observed;

        float recovered = (observed - (1f - alpha) * background) / alpha;
        return (byte)Math.Clamp((int)Math.Round(recovered), 0, 255);
    }
}
