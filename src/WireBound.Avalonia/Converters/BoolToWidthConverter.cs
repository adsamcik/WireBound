using System.Globalization;
using Avalonia.Data.Converters;

namespace WireBound.Avalonia.Converters;

/// <summary>
/// Conditional width converter: returns the converter parameter (parsed as
/// double) when the bound bool is <c>true</c>, otherwise zero. Used by the
/// Apps view's grouping indent — group members get a 32px nudge, solos and
/// heads get nothing.
/// </summary>
public sealed class BoolToWidthConverter : IValueConverter
{
    public static readonly BoolToWidthConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var width = parameter switch
        {
            string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            double d => d,
            int i => (double)i,
            _ => 32.0,
        };
        return value is true ? width : 0.0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
