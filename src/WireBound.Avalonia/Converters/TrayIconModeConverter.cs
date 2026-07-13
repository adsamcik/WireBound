using System;
using System.Globalization;
using Avalonia.Data.Converters;
using WireBound.Core.Models;

namespace WireBound.Avalonia.Converters;

/// <summary>
/// Converts <see cref="TrayIconMode"/> enum values to user-friendly display strings.
/// </summary>
public class TrayIconModeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is TrayIconMode mode)
        {
            return mode switch
            {
                TrayIconMode.AppIcon => "Application Icon",
                TrayIconMode.Traffic => "Network Traffic",
                TrayIconMode.Cpu => "CPU Usage",
                TrayIconMode.Ram => "RAM Usage",
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
