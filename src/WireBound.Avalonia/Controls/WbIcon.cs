using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace WireBound.Avalonia.Controls;

/// <summary>
/// Renders a single icon from the WireBound icon set. Looks up the
/// <see cref="IconKey"/> via <see cref="Converters.TintedIconConverter"/> and
/// draws the matching generated raster (<c>Assets/Icons/wb-*.png</c>) inside an
/// <see cref="Avalonia.Controls.Image"/> with high-quality interpolation, so the
/// same asset scales cleanly between 14/20/24/48 px.
///
/// <para>
/// Semantic icons (download, upload, CPU, memory, status) are recoloured to their
/// theme brush by default. Set <see cref="Tint"/> to override the colour for a
/// specific usage; leave it unset to use the per-key default (or the native raster
/// colour for icons with no semantic colour).
/// </para>
/// </summary>
public sealed class WbIcon : TemplatedControl
{
    public static readonly StyledProperty<string?> IconKeyProperty =
        AvaloniaProperty.Register<WbIcon, string?>(nameof(IconKey));

    public static readonly StyledProperty<IBrush?> TintProperty =
        AvaloniaProperty.Register<WbIcon, IBrush?>(nameof(Tint));

    /// <summary>
    /// Resource key into the raster icon set, e.g. <c>"WbNavOverview"</c>.
    /// </summary>
    public string? IconKey
    {
        get => GetValue(IconKeyProperty);
        set => SetValue(IconKeyProperty, value);
    }

    /// <summary>
    /// Optional colour override. When set, the icon is recoloured to this brush;
    /// when unset, the icon uses its per-key default colour or native raster colour.
    /// </summary>
    public IBrush? Tint
    {
        get => GetValue(TintProperty);
        set => SetValue(TintProperty, value);
    }
}
