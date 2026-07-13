using System.Globalization;
using Avalonia.Data.Converters;

namespace WireBound.Avalonia.Converters;

/// <summary>
/// Converts a bool to one of two double values supplied as a pipe-separated
/// converter parameter, <c>"trueValue|falseValue"</c>. Example use:
/// <c>Opacity="{Binding IsCurrentlyRunning, Converter=..., ConverterParameter='1.0|0.55'}"</c>
/// makes a row fully opaque when running and 55% opaque when idle.
/// Returns 1.0 for both states if the parameter is missing or malformed,
/// which is the safest fallback for opacity bindings.
/// </summary>
public sealed class BoolToDoubleConverter : IValueConverter
{
    public static readonly BoolToDoubleConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var (trueVal, falseVal) = ParseParam(parameter);
        return value is true ? trueVal : falseVal;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static (double trueVal, double falseVal) ParseParam(object? parameter)
    {
        if (parameter is not string s) return (1.0, 1.0);
        var parts = s.Split('|', StringSplitOptions.None);
        if (parts.Length != 2) return (1.0, 1.0);
        var ok1 = double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var t);
        var ok2 = double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var f);
        if (!ok1 || !ok2) return (1.0, 1.0);
        return (t, f);
    }
}
