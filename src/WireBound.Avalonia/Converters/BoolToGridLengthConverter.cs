using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace WireBound.Avalonia.Converters;

/// <summary>
/// Returns a fixed-pixel <see cref="GridLength"/> when the bound bool is
/// <c>true</c>, otherwise <see cref="GridLength.Auto"/>'s zero-pixel
/// equivalent (<c>new GridLength(0)</c>). Used by the Apps view to bind
/// each column's <c>ColumnDefinition.Width</c> to a visibility flag, so
/// hidden columns collapse fully instead of leaving an empty gap.
/// </summary>
public sealed class BoolToGridLengthConverter : IValueConverter
{
    public static readonly BoolToGridLengthConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var width = parameter switch
        {
            string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            double d => d,
            int i => (double)i,
            _ => 100.0,
        };
        return value is true ? new GridLength(width) : new GridLength(0);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
