using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace WireBound.Avalonia.Controls;

/// <summary>
/// Size variants for the CircularGauge control.
/// </summary>
public enum GaugeSize
{
    /// <summary>40px diameter, 4px ring thickness.</summary>
    Compact,
    /// <summary>64px diameter, 6px ring thickness.</summary>
    Normal,
    /// <summary>96px diameter, 6px ring thickness.</summary>
    Large
}

/// <summary>
/// A circular progress gauge control that displays a percentage value (0-100%)
/// with an optional label.
/// </summary>
public partial class CircularGauge : UserControl
{
    #region Styled Properties

    /// <summary>
    /// Defines the <see cref="Value"/> property.
    /// </summary>
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<CircularGauge, double>(
            nameof(Value),
            defaultValue: 0,
            coerce: CoerceValue);

    /// <summary>
    /// Defines the <see cref="GaugeColor"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> GaugeColorProperty =
        AvaloniaProperty.Register<CircularGauge, IBrush?>(
            nameof(GaugeColor),
            defaultValue: Brushes.Cyan);

    /// <summary>
    /// Defines the <see cref="BackgroundColor"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> BackgroundColorProperty =
        AvaloniaProperty.Register<CircularGauge, IBrush?>(
            nameof(BackgroundColor),
            defaultValue: null);

    /// <summary>
    /// Defines the <see cref="Size"/> property.
    /// </summary>
    public static readonly StyledProperty<GaugeSize> SizeProperty =
        AvaloniaProperty.Register<CircularGauge, GaugeSize>(
            nameof(Size),
            defaultValue: GaugeSize.Normal);

    /// <summary>
    /// Defines the <see cref="Label"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<CircularGauge, string?>(
            nameof(Label),
            defaultValue: null);

    /// <summary>
    /// Defines the <see cref="ShowLabel"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> ShowLabelProperty =
        AvaloniaProperty.Register<CircularGauge, bool>(
            nameof(ShowLabel),
            defaultValue: true);

    #endregion

    #region Read-Only Properties (Computed)

    /// <summary>
    /// Defines the <see cref="SweepAngle"/> property.
    /// </summary>
    public static readonly DirectProperty<CircularGauge, double> SweepAngleProperty =
        AvaloniaProperty.RegisterDirect<CircularGauge, double>(
            nameof(SweepAngle),
            o => o.SweepAngle);

    /// <summary>
    /// Defines the <see cref="PercentageText"/> property.
    /// </summary>
    public static readonly DirectProperty<CircularGauge, string> PercentageTextProperty =
        AvaloniaProperty.RegisterDirect<CircularGauge, string>(
            nameof(PercentageText),
            o => o.PercentageText);

    /// <summary>
    /// Defines the <see cref="GaugeDiameter"/> property.
    /// </summary>
    public static readonly DirectProperty<CircularGauge, double> GaugeDiameterProperty =
        AvaloniaProperty.RegisterDirect<CircularGauge, double>(
            nameof(GaugeDiameter),
            o => o.GaugeDiameter);

    /// <summary>
    /// Defines the <see cref="RingThickness"/> property.
    /// </summary>
    public static readonly DirectProperty<CircularGauge, double> RingThicknessProperty =
        AvaloniaProperty.RegisterDirect<CircularGauge, double>(
            nameof(RingThickness),
            o => o.RingThickness);

    /// <summary>
    /// Defines the <see cref="PercentageFontSize"/> property.
    /// </summary>
    public static readonly DirectProperty<CircularGauge, double> PercentageFontSizeProperty =
        AvaloniaProperty.RegisterDirect<CircularGauge, double>(
            nameof(PercentageFontSize),
            o => o.PercentageFontSize);

    /// <summary>
    /// Defines the <see cref="LabelFontSize"/> property.
    /// </summary>
    public static readonly DirectProperty<CircularGauge, double> LabelFontSizeProperty =
        AvaloniaProperty.RegisterDirect<CircularGauge, double>(
            nameof(LabelFontSize),
            o => o.LabelFontSize);

    /// <summary>
    /// Defines the <see cref="TextCenterX"/> property.
    /// </summary>
    public static readonly DirectProperty<CircularGauge, double> TextCenterXProperty =
        AvaloniaProperty.RegisterDirect<CircularGauge, double>(
            nameof(TextCenterX),
            o => o.TextCenterX);

    /// <summary>
    /// Defines the <see cref="TextCenterY"/> property.
    /// </summary>
    public static readonly DirectProperty<CircularGauge, double> TextCenterYProperty =
        AvaloniaProperty.RegisterDirect<CircularGauge, double>(
            nameof(TextCenterY),
            o => o.TextCenterY);

    #endregion

    #region Private Fields

    private double _sweepAngle;
    private string _percentageText = "0%";
    private double _gaugeDiameter = 64;
    private double _ringThickness = 6;
    private double _percentageFontSize = 14;
    private double _labelFontSize = 10;
    private double _textCenterX;
    private double _textCenterY;

    #endregion

    #region Constructor

    public CircularGauge()
    {
        InitializeComponent();
        UpdateComputedProperties();
        ApplySizeClass();
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets or sets the gauge value (0-100).
    /// </summary>
    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>
    /// Gets or sets the color of the progress arc.
    /// </summary>
    public IBrush? GaugeColor
    {
        get => GetValue(GaugeColorProperty);
        set => SetValue(GaugeColorProperty, value);
    }

    /// <summary>
    /// Gets or sets the color of the background arc.
    /// Defaults to SurfaceElevatedColor from theme if not set.
    /// </summary>
    public IBrush? BackgroundColor
    {
        get
        {
            var value = GetValue(BackgroundColorProperty);
            if (value is not null)
                return value;

            // Try to get from theme resources
            if (this.TryFindResource("SurfaceElevatedColor", out var resource) && resource is Color c)
                return new SolidColorBrush(c);

            // Fallback to hardcoded value
            return new SolidColorBrush(Color.FromRgb(62, 92, 118));
        }
        set => SetValue(BackgroundColorProperty, value);
    }

    /// <summary>
    /// Gets or sets the size variant of the gauge.
    /// </summary>
    public GaugeSize Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>
    /// Gets or sets the label text shown below the percentage.
    /// </summary>
    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the label is visible.
    /// </summary>
    public bool ShowLabel
    {
        get => GetValue(ShowLabelProperty);
        set => SetValue(ShowLabelProperty, value);
    }

    /// <summary>
    /// Gets the sweep angle for the progress arc (computed from Value).
    /// </summary>
    public double SweepAngle
    {
        get => _sweepAngle;
        private set => SetAndRaise(SweepAngleProperty, ref _sweepAngle, value);
    }

    /// <summary>
    /// Gets the formatted percentage text (e.g., "78%").
    /// </summary>
    public string PercentageText
    {
        get => _percentageText;
        private set => SetAndRaise(PercentageTextProperty, ref _percentageText, value);
    }

    /// <summary>
    /// Gets the diameter of the gauge based on Size.
    /// </summary>
    public double GaugeDiameter
    {
        get => _gaugeDiameter;
        private set => SetAndRaise(GaugeDiameterProperty, ref _gaugeDiameter, value);
    }

    /// <summary>
    /// Gets the ring thickness based on Size.
    /// </summary>
    public double RingThickness
    {
        get => _ringThickness;
        private set => SetAndRaise(RingThicknessProperty, ref _ringThickness, value);
    }

    /// <summary>
    /// Gets the font size for the percentage text based on Size.
    /// </summary>
    public double PercentageFontSize
    {
        get => _percentageFontSize;
        private set => SetAndRaise(PercentageFontSizeProperty, ref _percentageFontSize, value);
    }

    /// <summary>
    /// Gets the font size for the label text based on Size.
    /// </summary>
    public double LabelFontSize
    {
        get => _labelFontSize;
        private set => SetAndRaise(LabelFontSizeProperty, ref _labelFontSize, value);
    }

    /// <summary>
    /// Gets the X position for centering the percentage text.
    /// </summary>
    public double TextCenterX
    {
        get => _textCenterX;
        private set => SetAndRaise(TextCenterXProperty, ref _textCenterX, value);
    }

    /// <summary>
    /// Gets the Y position for centering the percentage text.
    /// </summary>
    public double TextCenterY
    {
        get => _textCenterY;
        private set => SetAndRaise(TextCenterYProperty, ref _textCenterY, value);
    }

    #endregion

    #region Property Changed Handlers

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ValueProperty)
        {
            UpdateValueProperties();
        }
        else if (change.Property == SizeProperty)
        {
            UpdateSizeProperties();
            ApplySizeClass();
        }
    }

    #endregion

    #region Private Methods

    private static double CoerceValue(AvaloniaObject obj, double value)
    {
        return Math.Clamp(value, 0, 100);
    }

    private void UpdateComputedProperties()
    {
        UpdateValueProperties();
        UpdateSizeProperties();
    }

    private void UpdateValueProperties()
    {
        // Calculate sweep angle (360 degrees = 100%)
        SweepAngle = Value / 100.0 * 360.0;
        PercentageText = $"{Value:F0}%";
    }

    private void UpdateSizeProperties()
    {
        switch (Size)
        {
            case GaugeSize.Compact:
                GaugeDiameter = 40;
                RingThickness = 4;
                PercentageFontSize = 10;
                LabelFontSize = 8;
                break;

            case GaugeSize.Normal:
                GaugeDiameter = 64;
                RingThickness = 6;
                PercentageFontSize = 14;
                LabelFontSize = 10;
                break;

            case GaugeSize.Large:
                GaugeDiameter = 96;
                RingThickness = 6;
                PercentageFontSize = 20;
                LabelFontSize = 12;
                break;
        }

        // Update text centering - will be adjusted after measure
        UpdateTextPosition();
    }

    private void UpdateTextPosition()
    {
        // Estimate text width based on font size and typical percentage string
        var estimatedTextWidth = PercentageFontSize * 2.5;
        var estimatedTextHeight = PercentageFontSize * 1.2;

        TextCenterX = (GaugeDiameter - estimatedTextWidth) / 2;
        TextCenterY = (GaugeDiameter - estimatedTextHeight) / 2;
    }

    private void ApplySizeClass()
    {
        // Remove existing size classes
        Classes.Remove("Compact");
        Classes.Remove("Normal");
        Classes.Remove("Large");

        // Add the appropriate size class
        var sizeClass = Size switch
        {
            GaugeSize.Compact => "Compact",
            GaugeSize.Normal => "Normal",
            GaugeSize.Large => "Large",
            _ => "Normal"
        };
        Classes.Add(sizeClass);
    }

    #endregion
}
