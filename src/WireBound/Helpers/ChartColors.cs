using SkiaSharp;

namespace WireBound.Helpers;

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
    // HELPER METHODS
    // ═══════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Creates a download color with specified alpha for gradient fills.
    /// </summary>
    public static SKColor DownloadWithAlpha(byte alpha) => DownloadAccentColor.WithAlpha(alpha);
    
    /// <summary>
    /// Creates an upload color with specified alpha for gradient fills.
    /// </summary>
    public static SKColor UploadWithAlpha(byte alpha) => UploadAccentColor.WithAlpha(alpha);
}
