using System.Globalization;
using Avalonia.Data.Converters;

namespace WireBound.Avalonia.Converters;

/// <summary>
/// Converts a bool to a rotation angle for chevron disclosure indicators.
/// <c>false</c> → 0° (chevron points right, collapsed),
/// <c>true</c> → 90° (chevron points down, expanded).
/// </summary>
public sealed class BoolToAngleConverter : IValueConverter
{
    public static readonly BoolToAngleConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? 90.0 : 0.0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
