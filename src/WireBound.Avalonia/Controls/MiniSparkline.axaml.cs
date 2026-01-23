using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace WireBound.Avalonia.Controls;

/// <summary>
/// A lightweight inline sparkline control for displaying trend data.
/// Designed for use in cards and compact UI elements.
/// </summary>
public partial class MiniSparkline : UserControl
{
    #region Styled Properties

    /// <summary>
    /// Defines the <see cref="Values"/> property.
    /// </summary>
    public static readonly StyledProperty<IList?> ValuesProperty =
        AvaloniaProperty.Register<MiniSparkline, IList?>(
            nameof(Values),
            defaultValue: null);

    /// <summary>
    /// Defines the <see cref="StrokeColor"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> StrokeColorProperty =
        AvaloniaProperty.Register<MiniSparkline, IBrush?>(
            nameof(StrokeColor),
            defaultValue: null);

    /// <summary>
    /// Defines the <see cref="FillColor"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> FillColorProperty =
        AvaloniaProperty.Register<MiniSparkline, IBrush?>(
            nameof(FillColor),
            defaultValue: null);

    /// <summary>
    /// Defines the <see cref="StrokeThickness"/> property.
    /// </summary>
    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<MiniSparkline, double>(
            nameof(StrokeThickness),
            defaultValue: 1.5);

    /// <summary>
    /// Defines the <see cref="MaxPoints"/> property.
    /// </summary>
    public static readonly StyledProperty<int> MaxPointsProperty =
        AvaloniaProperty.Register<MiniSparkline, int>(
            nameof(MaxPoints),
            defaultValue: 30,
            coerce: CoerceMaxPoints);

    /// <summary>
    /// Defines the <see cref="MinValue"/> property for manual Y-axis scaling.
    /// </summary>
    public static readonly StyledProperty<double?> MinValueProperty =
        AvaloniaProperty.Register<MiniSparkline, double?>(
            nameof(MinValue),
            defaultValue: null);

    /// <summary>
    /// Defines the <see cref="MaxValue"/> property for manual Y-axis scaling.
    /// </summary>
    public static readonly StyledProperty<double?> MaxValueProperty =
        AvaloniaProperty.Register<MiniSparkline, double?>(
            nameof(MaxValue),
            defaultValue: null);

    #endregion

    #region Direct Properties (Computed)

    /// <summary>
    /// Defines the <see cref="LinePoints"/> property.
    /// </summary>
    public static readonly DirectProperty<MiniSparkline, Points> LinePointsProperty =
        AvaloniaProperty.RegisterDirect<MiniSparkline, Points>(
            nameof(LinePoints),
            o => o.LinePoints);

    /// <summary>
    /// Defines the <see cref="FillPoints"/> property.
    /// </summary>
    public static readonly DirectProperty<MiniSparkline, Points> FillPointsProperty =
        AvaloniaProperty.RegisterDirect<MiniSparkline, Points>(
            nameof(FillPoints),
            o => o.FillPoints);

    /// <summary>
    /// Defines the <see cref="ShowFill"/> property.
    /// </summary>
    public static readonly DirectProperty<MiniSparkline, bool> ShowFillProperty =
        AvaloniaProperty.RegisterDirect<MiniSparkline, bool>(
            nameof(ShowFill),
            o => o.ShowFill);

    #endregion

    #region Private Fields

    private Points _linePoints = new();
    private Points _fillPoints = new();
    private bool _showFill;
    private INotifyCollectionChanged? _subscribedCollection;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="MiniSparkline"/> class.
    /// </summary>
    public MiniSparkline()
    {
        InitializeComponent();
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets or sets the data values to display in the sparkline.
    /// Supports any IList containing numeric values (double, int, float, etc.).
    /// </summary>
    public IList? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    /// <summary>
    /// Gets or sets the stroke color of the sparkline.
    /// Defaults to PrimaryColor from theme if not set.
    /// </summary>
    public IBrush? StrokeColor
    {
        get => GetValue(StrokeColorProperty);
        set => SetValue(StrokeColorProperty, value);
    }

    /// <summary>
    /// Gets or sets the fill color (gradient) below the sparkline.
    /// If null, no fill is shown.
    /// </summary>
    public IBrush? FillColor
    {
        get => GetValue(FillColorProperty);
        set => SetValue(FillColorProperty, value);
    }

    /// <summary>
    /// Gets or sets the stroke thickness of the sparkline.
    /// Default is 1.5.
    /// </summary>
    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum number of data points to display.
    /// Default is 30. Minimum is 2.
    /// </summary>
    public int MaxPoints
    {
        get => GetValue(MaxPointsProperty);
        set => SetValue(MaxPointsProperty, value);
    }

    /// <summary>
    /// Gets or sets the minimum Y-axis value.
    /// If null, auto-scales to data minimum.
    /// </summary>
    public double? MinValue
    {
        get => GetValue(MinValueProperty);
        set => SetValue(MinValueProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum Y-axis value.
    /// If null, auto-scales to data maximum.
    /// </summary>
    public double? MaxValue
    {
        get => GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
    }

    /// <summary>
    /// Gets the computed points for the sparkline polyline.
    /// </summary>
    public Points LinePoints
    {
        get => _linePoints;
        private set => SetAndRaise(LinePointsProperty, ref _linePoints, value);
    }

    /// <summary>
    /// Gets the computed points for the fill polygon.
    /// </summary>
    public Points FillPoints
    {
        get => _fillPoints;
        private set => SetAndRaise(FillPointsProperty, ref _fillPoints, value);
    }

    /// <summary>
    /// Gets whether the fill should be shown.
    /// </summary>
    public bool ShowFill
    {
        get => _showFill;
        private set => SetAndRaise(ShowFillProperty, ref _showFill, value);
    }

    #endregion

    #region Overrides

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ValuesProperty)
        {
            // Unsubscribe from old collection
            if (_subscribedCollection is not null)
            {
                _subscribedCollection.CollectionChanged -= OnValuesCollectionChanged;
                _subscribedCollection = null;
            }

            // Subscribe to new collection if it supports change notification
            if (change.NewValue is INotifyCollectionChanged notifyCollection)
            {
                _subscribedCollection = notifyCollection;
                _subscribedCollection.CollectionChanged += OnValuesCollectionChanged;
            }

            UpdatePoints();
        }
        else if (change.Property == MaxPointsProperty ||
                 change.Property == MinValueProperty ||
                 change.Property == MaxValueProperty ||
                 change.Property == BoundsProperty)
        {
            UpdatePoints();
        }
        else if (change.Property == FillColorProperty)
        {
            ShowFill = FillColor is not null;
        }
    }

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Apply default stroke color from theme if not set
        if (StrokeColor is null)
        {
            if (this.TryFindResource("PrimaryBrush", ActualThemeVariant, out var resource) &&
                resource is IBrush brush)
            {
                StrokeColor = brush;
            }
            else
            {
                StrokeColor = new SolidColorBrush(Color.Parse("#00E5FF"));
            }
        }

        UpdatePoints();
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        // Clean up subscription
        if (_subscribedCollection is not null)
        {
            _subscribedCollection.CollectionChanged -= OnValuesCollectionChanged;
            _subscribedCollection = null;
        }
    }

    #endregion

    #region Private Methods

    private static int CoerceMaxPoints(AvaloniaObject sender, int value)
    {
        return Math.Max(2, value);
    }

    private void OnValuesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdatePoints();
    }

    private void UpdatePoints()
    {
        var values = Values;
        if (values is null || values.Count == 0)
        {
            LinePoints = new Points();
            FillPoints = new Points();
            return;
        }

        // Get display bounds
        double width = Bounds.Width;
        double height = Bounds.Height;

        if (width <= 0 || height <= 0)
        {
            return;
        }

        // Extract numeric values
        var numericValues = ExtractNumericValues(values);
        if (numericValues.Count == 0)
        {
            LinePoints = new Points();
            FillPoints = new Points();
            return;
        }

        // Take only the last MaxPoints values
        int maxPoints = MaxPoints;
        if (numericValues.Count > maxPoints)
        {
            numericValues = numericValues.GetRange(numericValues.Count - maxPoints, maxPoints);
        }

        // Calculate Y-axis bounds
        double minVal = MinValue ?? double.MaxValue;
        double maxVal = MaxValue ?? double.MinValue;

        if (!MinValue.HasValue || !MaxValue.HasValue)
        {
            foreach (var val in numericValues)
            {
                if (!MinValue.HasValue && val < minVal) minVal = val;
                if (!MaxValue.HasValue && val > maxVal) maxVal = val;
            }
        }

        // Ensure we have a valid range
        if (Math.Abs(maxVal - minVal) < 0.0001)
        {
            minVal -= 0.5;
            maxVal += 0.5;
        }

        double range = maxVal - minVal;

        // Calculate padding for stroke thickness
        double strokePadding = StrokeThickness / 2;
        double drawHeight = height - (strokePadding * 2);
        double drawWidth = width;

        // Calculate points
        int pointCount = numericValues.Count;
        double xStep = pointCount > 1 ? drawWidth / (pointCount - 1) : drawWidth;

        var linePoints = new Points();
        var fillPoints = new Points();

        for (int i = 0; i < pointCount; i++)
        {
            double x = pointCount > 1 ? i * xStep : drawWidth / 2;
            double normalizedY = (numericValues[i] - minVal) / range;
            double y = strokePadding + drawHeight * (1 - normalizedY); // Invert Y for screen coords

            linePoints.Add(new Point(x, y));
            fillPoints.Add(new Point(x, y));
        }

        // Complete the fill polygon by adding bottom corners
        if (fillPoints.Count > 0)
        {
            fillPoints.Add(new Point(fillPoints[^1].X, height));
            fillPoints.Add(new Point(fillPoints[0].X, height));
        }

        LinePoints = linePoints;
        FillPoints = fillPoints;
    }

    private static List<double> ExtractNumericValues(IList values)
    {
        var result = new List<double>(values.Count);

        foreach (var item in values)
        {
            double? numericValue = item switch
            {
                double d => d,
                float f => f,
                int i => i,
                long l => l,
                decimal m => (double)m,
                short s => s,
                byte b => b,
                _ => null
            };

            if (numericValue.HasValue && !double.IsNaN(numericValue.Value) && !double.IsInfinity(numericValue.Value))
            {
                result.Add(numericValue.Value);
            }
        }

        return result;
    }

    #endregion
}
