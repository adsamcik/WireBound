using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace WireBound.Avalonia.Converters;

/// <summary>
/// Converts insights period values (e.g. "ThisWeek") to user-friendly display strings (e.g. "This Week").
/// </summary>
public class InsightsPeriodConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.ToString() switch
        {
            "Today" => "Today",
            "ThisWeek" => "This Week",
            "ThisMonth" => "This Month",
            _ => value?.ToString()
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return global::Avalonia.Data.BindingOperations.DoNothing;
    }
}
