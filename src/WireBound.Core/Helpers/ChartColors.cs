using SkiaSharp;

namespace WireBound.Core.Helpers;

/// <summary>
/// Centralized chart color definitions for LiveCharts2/SkiaSharp.
/// Ensures consistent theming across all chart visualizations.
/// These match the XAML color resources defined in Colors.xaml.
/// Updated for WireBound Design System v3.0 - "Signal & Flow" theme.
/// </summary>
public static class ChartColors
{
    // ═══════════════════════════════════════════════════════════════════════
    // NETWORK ACTIVITY COLORS (matching new design system)
    // ═══════════════════════════════════════════════════════════════════════

    // Download: softened signal cyan
    public static SKColor DownloadColor => new(102, 215, 229);       // #66D7E5
    public static SKColor DownloadAccentColor => new(139, 228, 236); // #8BE4EC
    public static SKColor DownloadDimColor => new(49, 154, 169);     // #319AA9

    // Upload: warm coral
    public static SKColor UploadColor => new(240, 163, 132);        // #F0A384
    public static SKColor UploadAccentColor => new(247, 193, 170);  // #F7C1AA
    public static SKColor UploadDimColor => new(200, 116, 86);      // #C87456

    // ═══════════════════════════════════════════════════════════════════════
    // SYSTEM RESOURCE OVERLAY COLORS
    // ═══════════════════════════════════════════════════════════════════════

    // CPU: softened periwinkle blue
    public static SKColor CpuColor => new(131, 169, 249);          // #83A9F9
    public static SKColor CpuAccentColor => new(175, 197, 251);    // #AFC5FB
    public static SKColor CpuDimColor => new(94, 131, 214);        // #5E83D6

    // Memory: Magenta Pink (colorblind-safe — distinct from CPU blue, which a
    // red-green deficiency would otherwise collapse a violet toward). Warmth +
    // lightness keep it separable from the blues under deuteranopia/protanopia.
    public static SKColor MemoryColor => new(240, 139, 190);       // #F08BBE
    public static SKColor MemoryAccentColor => new(245, 179, 211); // #F5B3D3
    public static SKColor MemoryDimColor => new(201, 92, 145);     // #C95C91

    // Disk: Amber Gold
    public static SKColor DiskColor => new(242, 198, 109);         // #F2C66D
    public static SKColor DiskReadColor => new(242, 198, 109);     // #F2C66D
    public static SKColor DiskWriteColor => new(201, 154, 63);     // #C99A3F

    // ═══════════════════════════════════════════════════════════════════════
    // CHART AXIS & GRID COLORS
    // ═══════════════════════════════════════════════════════════════════════

    public static SKColor AxisLabelColor => new(124, 137, 152);    // #7C8998 - muted text
    public static SKColor AxisNameColor => new(178, 189, 202);     // #B2BDCA - secondary text
    public static SKColor GridLineColor => new(38, 51, 66, 100);   // #263342 with alpha

    // ═══════════════════════════════════════════════════════════════════════
    // SECTION & THRESHOLD COLORS
    // ═══════════════════════════════════════════════════════════════════════

    public static SKColor SectionStrokeColor => new(38, 51, 66, 150);
    public static SKColor WarningSectionColor => new(242, 198, 109, 60);

    // ═══════════════════════════════════════════════════════════════════════
    // BACKGROUND & TOOLTIP COLORS
    // ═══════════════════════════════════════════════════════════════════════

    public static SKColor ChartBackgroundColor => new(22, 31, 42);      // #161F2A
    public static SKColor TooltipBackgroundColor => new(27, 38, 51);    // #1B2633
    public static SKColor TooltipTextColor => new(244, 247, 250);       // #F4F7FA

    // ═══════════════════════════════════════════════════════════════════════
    // MULTI-SERIES PALETTE
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A palette of colors for multi-series charts (e.g., per-app usage).
    /// Uses the Okabe-Ito colorblind-safe categorical palette (with black
    /// swapped for a light grey so it reads on the dark theme), so series stay
    /// distinguishable under deuteranopia, protanopia, and tritanopia.
    /// </summary>
    public static readonly SKColor[] SeriesPalette =
    [
        new(86, 180, 233),   // #56B4E9 Sky Blue
        new(230, 159, 0),    // #E69F00 Orange
        new(0, 158, 115),    // #009E73 Bluish Green
        new(204, 121, 167),  // #CC79A7 Reddish Purple
        new(240, 228, 66),   // #F0E442 Yellow
        new(0, 114, 178),    // #0072B2 Blue
        new(213, 94, 0),     // #D55E00 Vermilion
        new(191, 191, 191),  // #BFBFBF Light Grey
    ];
}
