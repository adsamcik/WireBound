using System;
using System.Globalization;
using Avalonia.Data.Converters;
using WireBound.Core.Models;

namespace WireBound.Avalonia.Converters;

/// <summary>
/// Converts SpeedUnit enum values to user-friendly display strings
/// </summary>
public class SpeedUnitConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is SpeedUnit unit)
        {
            return unit switch
            {
                SpeedUnit.BytesPerSecond => "MB/s",
                SpeedUnit.BitsPerSecond => "Mbps",
                _ => value.ToString()
            };
        }
        return value?.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return global::Avalonia.Data.BindingOperations.DoNothing;
    }
}
