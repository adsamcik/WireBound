using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace WireBound.Avalonia.Converters;

/// <summary>
/// Converter that returns a highlight brush when the current item matches the selected item
/// </summary>
public class SelectedRowBackgroundConverter : IMultiValueConverter
{
    public static readonly SelectedRowBackgroundConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2) return Brushes.Transparent;
        
        var currentDate = values[0] as DateOnly?;
        var selectedDate = values[1] as DateOnly?;
        
        if (currentDate.HasValue && selectedDate.HasValue && currentDate.Value == selectedDate.Value)
        {
            // Selected row background - subtle cyan tint
            return new SolidColorBrush(Color.Parse("#1500E5FF"));
        }
        
        return Brushes.Transparent;
    }
}

/// <summary>
/// Converter that returns a highlight border brush when the current item matches the selected item
/// </summary>
public class SelectedRowBorderConverter : IMultiValueConverter
{
    public static readonly SelectedRowBorderConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2) return Brushes.Transparent;
        
        var currentDate = values[0] as DateOnly?;
        var selectedDate = values[1] as DateOnly?;
        
        if (currentDate.HasValue && selectedDate.HasValue && currentDate.Value == selectedDate.Value)
        {
            // Selected row border - cyan accent
            return new SolidColorBrush(Color.Parse("#5000E5FF"));
        }
        
        return Brushes.Transparent;
    }
}

/// <summary>
/// Converter that returns rotation angle for chevron based on selection state
/// </summary>
public class SelectedRowChevronRotationConverter : IMultiValueConverter
{
    public static readonly SelectedRowChevronRotationConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2) return 0.0;
        
        var currentDate = values[0] as DateOnly?;
        var selectedDate = values[1] as DateOnly?;
        
        // Return 90 degrees rotation when selected (chevron points down)
        if (currentDate.HasValue && selectedDate.HasValue && currentDate.Value == selectedDate.Value)
        {
            return 90.0;
        }
        
        return 0.0;
    }
}

/// <summary>
/// Converter that returns opacity based on selection state
/// </summary>
public class SelectedRowChevronOpacityConverter : IMultiValueConverter
{
    public static readonly SelectedRowChevronOpacityConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2) return 0.5;
        
        var currentDate = values[0] as DateOnly?;
        var selectedDate = values[1] as DateOnly?;
        
        // Full opacity when selected
        if (currentDate.HasValue && selectedDate.HasValue && currentDate.Value == selectedDate.Value)
        {
            return 1.0;
        }
        
        return 0.5;
    }
}
