using SkiaSharp;

namespace WireBound.Core.Helpers;

/// <summary>
/// Centralized chart color definitions for LiveCharts2/SkiaSharp.
/// Ensures consistent theming across all chart visualizations.
/// These match the XAML color resources defined in Colors.xaml.
/// Updated for WireBound Design System v2.0 - "Deep Ocean" theme.
/// </summary>
public static class ChartColors
{
    // ═══════════════════════════════════════════════════════════════════════
    // NETWORK ACTIVITY COLORS (matching new design system)
    // ═══════════════════════════════════════════════════════════════════════
    
    // Download: Electric Cyan
    public static SKColor DownloadColor => new(0, 229, 255);       // #00E5FF - Primary download
    public static SKColor DownloadAccentColor => new(0, 212, 255); // #00D4FF - Download glow
    public static SKColor DownloadDimColor => new(0, 153, 170);    // #0099AA - Download dim
    
    // Upload: Coral Orange (new high-contrast choice)
    public static SKColor UploadColor => new(255, 107, 53);        // #FF6B35 - Primary upload
    public static SKColor UploadAccentColor => new(255, 140, 90);  // #FF8C5A - Upload glow
    public static SKColor UploadDimColor => new(204, 85, 41);      // #CC5529 - Upload dim
    
    // ═══════════════════════════════════════════════════════════════════════
    // CHART AXIS & GRID COLORS
    // ═══════════════════════════════════════════════════════════════════════
    
    public static SKColor AxisLabelColor => new(160, 168, 184);    // #A0A8B8 - matches SecondaryTextColor
    public static SKColor AxisNameColor => new(240, 235, 216);     // #F0EBD8 - matches PrimaryTextColor
    public static SKColor GridLineColor => new(42, 58, 94, 100);   // #2A3A5E with alpha - matches BorderColor
    
    // ═══════════════════════════════════════════════════════════════════════
    // SECTION & THRESHOLD COLORS
    // ═══════════════════════════════════════════════════════════════════════
    
    public static SKColor SectionStrokeColor => new(42, 58, 94, 150);   // BorderColor with more alpha
    public static SKColor WarningSectionColor => new(255, 182, 39, 60); // #FFB627 (WarningColor) with alpha
    
    // ═══════════════════════════════════════════════════════════════════════
    // BACKGROUND & TOOLTIP COLORS
    // ═══════════════════════════════════════════════════════════════════════
    
    public static SKColor ChartBackgroundColor => new(29, 45, 68);      // #1D2D44 - matches SurfaceColor
    public static SKColor TooltipBackgroundColor => new(15, 22, 32);    // #0F1620 - matches SidebarColor
    public static SKColor TooltipTextColor => new(240, 235, 216);       // #F0EBD8 - matches PrimaryTextColor
    
    // ═══════════════════════════════════════════════════════════════════════
    // MULTI-SERIES PALETTE
    // ═══════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// A palette of colors for multi-series charts (e.g., per-app usage).
    /// Designed to be distinguishable on dark backgrounds.
    /// </summary>
    public static readonly SKColor[] SeriesPalette =
    [
        new(0, 229, 255),    // Electric Cyan
        new(255, 107, 53),   // Coral Orange
        new(138, 201, 38),   // Lime Green
        new(255, 182, 39),   // Amber
        new(157, 78, 221),   // Purple
        new(255, 99, 132),   // Pink
        new(54, 162, 235),   // Blue
        new(75, 192, 192),   // Teal
    ];
}
