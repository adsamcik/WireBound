using Avalonia;
using Avalonia.Controls;
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
            // Selected row background - use theme resource
            if (Application.Current?.TryFindResource("SelectionBgBrush", out var brush) == true && brush is IBrush b)
                return b;
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
            // Selected row border - use theme resource
            if (Application.Current?.TryFindResource("SelectionBorderBrush", out var brush) == true && brush is IBrush b)
                return b;
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

/// <summary>
/// Converts a percentage (0-100) to a GridLength for proportional sizing
/// </summary>
public class PercentToGridLengthConverter : IValueConverter
{
    public static readonly PercentToGridLengthConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double percent)
        {
            var clamped = Math.Clamp(percent, 0, 100);
            return new GridLength(clamped, GridUnitType.Star);
        }
        return new GridLength(0, GridUnitType.Star);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a percentage (0-100) to remaining GridLength (100 - percent)
/// </summary>
public class PercentToRemainingGridLengthConverter : IValueConverter
{
    public static readonly PercentToRemainingGridLengthConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double percent)
        {
            var clamped = Math.Clamp(percent, 0, 100);
            return new GridLength(100 - clamped, GridUnitType.Star);
        }
        return new GridLength(100, GridUnitType.Star);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a percentage (0-100) to a relative width for progress bars
/// Returns a GridLength that represents the percentage of available width
/// </summary>
public class PercentToWidthConverter : IValueConverter
{
    public static readonly PercentToWidthConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double percent)
        {
            // Clamp to 0-100 range and return as a percentage string for Width binding
            var clamped = Math.Clamp(percent, 0, 100);
            // Return as a string that can be parsed as a relative width
            return $"{clamped}%";
        }
        return "0%";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a SortColumn enum to a sort indicator (↑ or ↓) when the column matches the parameter
/// </summary>
public class SortIndicatorConverter : IValueConverter
{
    public static readonly SortIndicatorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // This simple converter just returns an indicator if the column matches
        // The actual ascending/descending requires a MultiValueConverter
        if (value is WireBound.Avalonia.ViewModels.SortColumn currentColumn &&
            parameter is string columnName)
        {
            var paramColumn = columnName switch
            {
                "Date" => WireBound.Avalonia.ViewModels.SortColumn.Date,
                "Download" => WireBound.Avalonia.ViewModels.SortColumn.Download,
                "Upload" => WireBound.Avalonia.ViewModels.SortColumn.Upload,
                "Total" => WireBound.Avalonia.ViewModels.SortColumn.Total,
                _ => WireBound.Avalonia.ViewModels.SortColumn.Date
            };

            if (currentColumn == paramColumn)
            {
                return "●"; // Active indicator - direction shown separately
            }
        }
        return "";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts SortColumn + SortAscending to display the correct arrow indicator
/// </summary>
public class SortDirectionConverter : IMultiValueConverter
{
    public static readonly SortDirectionConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2) return "";

        if (values[0] is WireBound.Avalonia.ViewModels.SortColumn currentColumn &&
            values[1] is bool ascending &&
            parameter is string columnName)
        {
            var paramColumn = columnName switch
            {
                "Date" => WireBound.Avalonia.ViewModels.SortColumn.Date,
                "Download" => WireBound.Avalonia.ViewModels.SortColumn.Download,
                "Upload" => WireBound.Avalonia.ViewModels.SortColumn.Upload,
                "Total" => WireBound.Avalonia.ViewModels.SortColumn.Total,
                _ => WireBound.Avalonia.ViewModels.SortColumn.Date
            };

            if (currentColumn == paramColumn)
            {
                return ascending ? "↑" : "↓";
            }
        }
        return "";
    }
}
