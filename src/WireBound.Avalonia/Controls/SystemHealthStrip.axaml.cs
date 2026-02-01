using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace WireBound.Avalonia.Controls;

/// <summary>
/// A compact horizontal strip control that displays CPU, Memory, and optionally GPU metrics
/// using circular gauges with color-coded thresholds.
/// </summary>
public partial class SystemHealthStrip : UserControl
{
    #region Threshold Constants

    /// <summary>Threshold below which the metric is considered healthy (green).</summary>
    private const double WarningThreshold = 70.0;

    /// <summary>Threshold above which the metric is considered critical (red).</summary>
    private const double CriticalThreshold = 85.0;

    #endregion

    #region Styled Properties

    /// <summary>
    /// Defines the <see cref="CpuPercent"/> property.
    /// </summary>
    public static readonly StyledProperty<double> CpuPercentProperty =
        AvaloniaProperty.Register<SystemHealthStrip, double>(
            nameof(CpuPercent),
            defaultValue: 0,
            coerce: CoercePercent);

    /// <summary>
    /// Defines the <see cref="MemoryPercent"/> property.
    /// </summary>
    public static readonly StyledProperty<double> MemoryPercentProperty =
        AvaloniaProperty.Register<SystemHealthStrip, double>(
            nameof(MemoryPercent),
            defaultValue: 0,
            coerce: CoercePercent);

    /// <summary>
    /// Defines the <see cref="GpuPercent"/> property.
    /// </summary>
    public static readonly StyledProperty<double?> GpuPercentProperty =
        AvaloniaProperty.Register<SystemHealthStrip, double?>(
            nameof(GpuPercent),
            defaultValue: null);

    /// <summary>
    /// Defines the <see cref="ShowGpu"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> ShowGpuProperty =
        AvaloniaProperty.Register<SystemHealthStrip, bool>(
            nameof(ShowGpu),
            defaultValue: false);

    /// <summary>
    /// Defines the <see cref="IsExpanded"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<SystemHealthStrip, bool>(
            nameof(IsExpanded),
            defaultValue: false);

    /// <summary>
    /// Defines the <see cref="Command"/> property.
    /// </summary>
    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<SystemHealthStrip, ICommand?>(
            nameof(Command),
            defaultValue: null);

    /// <summary>
    /// Defines the <see cref="CommandParameter"/> property.
    /// </summary>
    public static readonly StyledProperty<object?> CommandParameterProperty =
        AvaloniaProperty.Register<SystemHealthStrip, object?>(
            nameof(CommandParameter),
            defaultValue: null);

    #endregion

    #region Read-Only Properties (Computed)

    /// <summary>
    /// Defines the <see cref="CpuPercentText"/> property.
    /// </summary>
    public static readonly DirectProperty<SystemHealthStrip, string> CpuPercentTextProperty =
        AvaloniaProperty.RegisterDirect<SystemHealthStrip, string>(
            nameof(CpuPercentText),
            o => o.CpuPercentText);

    /// <summary>
    /// Defines the <see cref="MemoryPercentText"/> property.
    /// </summary>
    public static readonly DirectProperty<SystemHealthStrip, string> MemoryPercentTextProperty =
        AvaloniaProperty.RegisterDirect<SystemHealthStrip, string>(
            nameof(MemoryPercentText),
            o => o.MemoryPercentText);

    /// <summary>
    /// Defines the <see cref="GpuPercentText"/> property.
    /// </summary>
    public static readonly DirectProperty<SystemHealthStrip, string> GpuPercentTextProperty =
        AvaloniaProperty.RegisterDirect<SystemHealthStrip, string>(
            nameof(GpuPercentText),
            o => o.GpuPercentText);

    /// <summary>
    /// Defines the <see cref="GpuDisplayPercent"/> property.
    /// </summary>
    public static readonly DirectProperty<SystemHealthStrip, double> GpuDisplayPercentProperty =
        AvaloniaProperty.RegisterDirect<SystemHealthStrip, double>(
            nameof(GpuDisplayPercent),
            o => o.GpuDisplayPercent);

    /// <summary>
    /// Defines the <see cref="CpuGaugeColor"/> property.
    /// </summary>
    public static readonly DirectProperty<SystemHealthStrip, IBrush?> CpuGaugeColorProperty =
        AvaloniaProperty.RegisterDirect<SystemHealthStrip, IBrush?>(
            nameof(CpuGaugeColor),
            o => o.CpuGaugeColor);

    /// <summary>
    /// Defines the <see cref="MemoryGaugeColor"/> property.
    /// </summary>
    public static readonly DirectProperty<SystemHealthStrip, IBrush?> MemoryGaugeColorProperty =
        AvaloniaProperty.RegisterDirect<SystemHealthStrip, IBrush?>(
            nameof(MemoryGaugeColor),
            o => o.MemoryGaugeColor);

    /// <summary>
    /// Defines the <see cref="GpuGaugeColor"/> property.
    /// </summary>
    public static readonly DirectProperty<SystemHealthStrip, IBrush?> GpuGaugeColorProperty =
        AvaloniaProperty.RegisterDirect<SystemHealthStrip, IBrush?>(
            nameof(GpuGaugeColor),
            o => o.GpuGaugeColor);

    #endregion

    #region Private Fields

    private string _cpuPercentText = "0%";
    private string _memoryPercentText = "0%";
    private string _gpuPercentText = "N/A";
    private double _gpuDisplayPercent;
    private IBrush? _cpuGaugeColor;
    private IBrush? _memoryGaugeColor;
    private IBrush? _gpuGaugeColor;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemHealthStrip"/> class.
    /// </summary>
    public SystemHealthStrip()
    {
        InitializeComponent();
        UpdateComputedProperties();
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets or sets the CPU usage percentage (0-100).
    /// </summary>
    public double CpuPercent
    {
        get => GetValue(CpuPercentProperty);
        set => SetValue(CpuPercentProperty, value);
    }

    /// <summary>
    /// Gets or sets the memory usage percentage (0-100).
    /// </summary>
    public double MemoryPercent
    {
        get => GetValue(MemoryPercentProperty);
        set => SetValue(MemoryPercentProperty, value);
    }

    /// <summary>
    /// Gets or sets the GPU usage percentage (0-100), or null if not available.
    /// </summary>
    public double? GpuPercent
    {
        get => GetValue(GpuPercentProperty);
        set => SetValue(GpuPercentProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the GPU section is visible.
    /// </summary>
    public bool ShowGpu
    {
        get => GetValue(ShowGpuProperty);
        set => SetValue(ShowGpuProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the control is in expanded view mode.
    /// </summary>
    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    /// <summary>
    /// Gets or sets the command to execute when the control is clicked.
    /// </summary>
    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    /// <summary>
    /// Gets or sets the parameter to pass to the command.
    /// </summary>
    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    /// <summary>
    /// Gets the formatted CPU percentage text.
    /// </summary>
    public string CpuPercentText
    {
        get => _cpuPercentText;
        private set => SetAndRaise(CpuPercentTextProperty, ref _cpuPercentText, value);
    }

    /// <summary>
    /// Gets the formatted memory percentage text.
    /// </summary>
    public string MemoryPercentText
    {
        get => _memoryPercentText;
        private set => SetAndRaise(MemoryPercentTextProperty, ref _memoryPercentText, value);
    }

    /// <summary>
    /// Gets the formatted GPU percentage text.
    /// </summary>
    public string GpuPercentText
    {
        get => _gpuPercentText;
        private set => SetAndRaise(GpuPercentTextProperty, ref _gpuPercentText, value);
    }

    /// <summary>
    /// Gets the GPU percentage for display (0 if null).
    /// </summary>
    public double GpuDisplayPercent
    {
        get => _gpuDisplayPercent;
        private set => SetAndRaise(GpuDisplayPercentProperty, ref _gpuDisplayPercent, value);
    }

    /// <summary>
    /// Gets the color brush for the CPU gauge based on threshold.
    /// </summary>
    public IBrush? CpuGaugeColor
    {
        get => _cpuGaugeColor;
        private set => SetAndRaise(CpuGaugeColorProperty, ref _cpuGaugeColor, value);
    }

    /// <summary>
    /// Gets the color brush for the memory gauge based on threshold.
    /// </summary>
    public IBrush? MemoryGaugeColor
    {
        get => _memoryGaugeColor;
        private set => SetAndRaise(MemoryGaugeColorProperty, ref _memoryGaugeColor, value);
    }

    /// <summary>
    /// Gets the color brush for the GPU gauge based on threshold.
    /// </summary>
    public IBrush? GpuGaugeColor
    {
        get => _gpuGaugeColor;
        private set => SetAndRaise(GpuGaugeColorProperty, ref _gpuGaugeColor, value);
    }

    #endregion

    #region Property Changed Handling

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == CpuPercentProperty ||
            change.Property == MemoryPercentProperty ||
            change.Property == GpuPercentProperty)
        {
            UpdateComputedProperties();
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Coerces the percentage value to be within 0-100.
    /// </summary>
    private static double CoercePercent(AvaloniaObject obj, double value)
    {
        return Math.Clamp(value, 0, 100);
    }

    /// <summary>
    /// Updates all computed properties based on current values.
    /// </summary>
    private void UpdateComputedProperties()
    {
        CpuPercentText = $"{CpuPercent:F0}%";
        MemoryPercentText = $"{MemoryPercent:F0}%";

        if (GpuPercent.HasValue)
        {
            GpuDisplayPercent = Math.Clamp(GpuPercent.Value, 0, 100);
            GpuPercentText = $"{GpuDisplayPercent:F0}%";
        }
        else
        {
            GpuDisplayPercent = 0;
            GpuPercentText = "N/A";
        }

        CpuGaugeColor = GetThresholdBrush(CpuPercent);
        MemoryGaugeColor = GetThresholdBrush(MemoryPercent);
        GpuGaugeColor = GpuPercent.HasValue ? GetThresholdBrush(GpuPercent.Value) : GetSuccessBrush();
    }

    /// <summary>
    /// Determines the appropriate color brush based on percentage thresholds.
    /// </summary>
    /// <param name="percent">The percentage value (0-100).</param>
    /// <returns>
    /// SuccessBrush (green) for values &lt; 70%,
    /// WarningBrush (yellow) for values 70-85%,
    /// ErrorBrush (red) for values &gt; 85%.
    /// </returns>
    private IBrush? GetThresholdBrush(double percent)
    {
        if (percent < WarningThreshold)
        {
            return GetSuccessBrush();
        }
        else if (percent <= CriticalThreshold)
        {
            return GetWarningBrush();
        }
        else
        {
            return GetErrorBrush();
        }
    }

    /// <summary>
    /// Gets the success brush from resources.
    /// </summary>
    private IBrush? GetSuccessBrush()
    {
        if (this.TryFindResource("SuccessBrush", out var brush) && brush is IBrush b)
            return b;
        return Brushes.Green;
    }

    /// <summary>
    /// Gets the warning brush from resources.
    /// </summary>
    private IBrush? GetWarningBrush()
    {
        if (this.TryFindResource("WarningBrush", out var brush) && brush is IBrush b)
            return b;
        return Brushes.Yellow;
    }

    /// <summary>
    /// Gets the error brush from resources.
    /// </summary>
    private IBrush? GetErrorBrush()
    {
        if (this.TryFindResource("ErrorBrush", out var brush) && brush is IBrush b)
            return b;
        return Brushes.Red;
    }

    #endregion

    #region Pointer Handling

    /// <inheritdoc/>
    protected override void OnPointerPressed(global::Avalonia.Input.PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (Command?.CanExecute(CommandParameter) == true)
        {
            Command.Execute(CommandParameter);
        }
    }

    #endregion
}
