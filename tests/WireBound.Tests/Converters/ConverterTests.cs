using AwesomeAssertions;
using Avalonia.Controls;
using Avalonia.Media;
using WireBound.Avalonia.Converters;
using WireBound.Core.Models;
using System.Globalization;

namespace WireBound.Tests.Converters;

/// <summary>
/// Unit tests for Avalonia value converters
/// </summary>
public class ConverterTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // SpeedUnitConverter - Convert
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void SpeedUnitConverter_Convert_BytesPerSecond_ReturnsMBs()
    {
        // Arrange
        var converter = new SpeedUnitConverter();

        // Act
        var result = converter.Convert(SpeedUnit.BytesPerSecond, typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        result.Should().Be("MB/s");
    }

    [Test]
    public void SpeedUnitConverter_Convert_BitsPerSecond_ReturnsMbps()
    {
        // Arrange
        var converter = new SpeedUnitConverter();

        // Act
        var result = converter.Convert(SpeedUnit.BitsPerSecond, typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        result.Should().Be("Mbps");
    }

    [Test]
    public void SpeedUnitConverter_Convert_Null_ReturnsNull()
    {
        // Arrange
        var converter = new SpeedUnitConverter();

        // Act
        var result = converter.Convert(null, typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        result.Should().BeNull();
    }

    [Test]
    public void SpeedUnitConverter_Convert_NonSpeedUnitValue_ReturnsToString()
    {
        // Arrange
        var converter = new SpeedUnitConverter();

        // Act
        var result = converter.Convert("SomeString", typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        result.Should().Be("SomeString");
    }

    [Test]
    public void SpeedUnitConverter_ConvertBack_ReturnsDoNothing()
    {
        // Arrange
        var converter = new SpeedUnitConverter();

        // Act
        var result = converter.ConvertBack("MB/s", typeof(SpeedUnit), null, CultureInfo.InvariantCulture);

        // Assert - should not throw and should return DoNothing
        result.Should().Be(global::Avalonia.Data.BindingOperations.DoNothing);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SelectedRowBackgroundConverter
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void SelectedRowBackgroundConverter_MatchingDates_ReturnsNonTransparent()
    {
        // Arrange
        var converter = SelectedRowBackgroundConverter.Instance;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var values = new List<object?> { (DateOnly?)today, (DateOnly?)today };

        // Act
        var result = converter.Convert(values, typeof(IBrush), null, CultureInfo.InvariantCulture);

        // Assert - should return a brush that is not Brushes.Transparent
        result.Should().NotBeNull();
        result.Should().BeAssignableTo<IBrush>();
        result.Should().NotBeSameAs(Brushes.Transparent);
    }

    [Test]
    public void SelectedRowBackgroundConverter_NonMatchingDates_ReturnsTransparent()
    {
        // Arrange
        var converter = SelectedRowBackgroundConverter.Instance;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var yesterday = today.AddDays(-1);
        var values = new List<object?> { (DateOnly?)today, (DateOnly?)yesterday };

        // Act
        var result = converter.Convert(values, typeof(IBrush), null, CultureInfo.InvariantCulture);

        // Assert
        result.Should().BeSameAs(Brushes.Transparent);
    }

    [Test]
    public void SelectedRowBackgroundConverter_NullDates_ReturnsTransparent()
    {
        // Arrange
        var converter = SelectedRowBackgroundConverter.Instance;
        var values = new List<object?> { null, null };

        // Act
        var result = converter.Convert(values, typeof(IBrush), null, CultureInfo.InvariantCulture);

        // Assert
        result.Should().BeSameAs(Brushes.Transparent);
    }

    [Test]
    public void SelectedRowBackgroundConverter_InsufficientValues_ReturnsTransparent()
    {
        // Arrange
        var converter = SelectedRowBackgroundConverter.Instance;
        var values = new List<object?> { (DateOnly?)DateOnly.FromDateTime(DateTime.Today) };

        // Act
        var result = converter.Convert(values, typeof(IBrush), null, CultureInfo.InvariantCulture);

        // Assert
        result.Should().BeSameAs(Brushes.Transparent);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SelectedRowChevronRotationConverter
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void SelectedRowChevronRotationConverter_MatchingDates_Returns90()
    {
        // Arrange
        var converter = SelectedRowChevronRotationConverter.Instance;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var values = new List<object?> { (DateOnly?)today, (DateOnly?)today };

        // Act
        var result = converter.Convert(values, typeof(double), null, CultureInfo.InvariantCulture);

        // Assert
        result.Should().Be(90.0);
    }

    [Test]
    public void SelectedRowChevronRotationConverter_NonMatchingDates_Returns0()
    {
        // Arrange
        var converter = SelectedRowChevronRotationConverter.Instance;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var yesterday = today.AddDays(-1);
        var values = new List<object?> { (DateOnly?)today, (DateOnly?)yesterday };

        // Act
        var result = converter.Convert(values, typeof(double), null, CultureInfo.InvariantCulture);

        // Assert
        result.Should().Be(0.0);
    }

    [Test]
    public void SelectedRowChevronRotationConverter_NullDates_Returns0()
    {
        // Arrange
        var converter = SelectedRowChevronRotationConverter.Instance;
        var values = new List<object?> { null, null };

        // Act
        var result = converter.Convert(values, typeof(double), null, CultureInfo.InvariantCulture);

        // Assert
        result.Should().Be(0.0);
    }

    [Test]
    public void SelectedRowChevronRotationConverter_InsufficientValues_Returns0()
    {
        // Arrange
        var converter = SelectedRowChevronRotationConverter.Instance;
        var values = new List<object?>();

        // Act
        var result = converter.Convert(values, typeof(double), null, CultureInfo.InvariantCulture);

        // Assert
        result.Should().Be(0.0);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SelectedRowChevronOpacityConverter
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void SelectedRowChevronOpacityConverter_MatchingDates_Returns1()
    {
        // Arrange
        var converter = SelectedRowChevronOpacityConverter.Instance;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var values = new List<object?> { (DateOnly?)today, (DateOnly?)today };

        // Act
        var result = converter.Convert(values, typeof(double), null, CultureInfo.InvariantCulture);

        // Assert
        result.Should().Be(1.0);
    }

    [Test]
    public void SelectedRowChevronOpacityConverter_NonMatchingDates_Returns05()
    {
        // Arrange
        var converter = SelectedRowChevronOpacityConverter.Instance;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var yesterday = today.AddDays(-1);
        var values = new List<object?> { (DateOnly?)today, (DateOnly?)yesterday };

        // Act
        var result = converter.Convert(values, typeof(double), null, CultureInfo.InvariantCulture);

        // Assert
        result.Should().Be(0.5);
    }

    [Test]
    public void SelectedRowChevronOpacityConverter_NullDates_Returns05()
    {
        // Arrange
        var converter = SelectedRowChevronOpacityConverter.Instance;
        var values = new List<object?> { null, null };

        // Act
        var result = converter.Convert(values, typeof(double), null, CultureInfo.InvariantCulture);

        // Assert
        result.Should().Be(0.5);
    }

    [Test]
    public void SelectedRowChevronOpacityConverter_InsufficientValues_Returns05()
    {
        // Arrange
        var converter = SelectedRowChevronOpacityConverter.Instance;
        var values = new List<object?>();

        // Act
        var result = converter.Convert(values, typeof(double), null, CultureInfo.InvariantCulture);

        // Assert
        result.Should().Be(0.5);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // PercentToGridLengthConverter
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void PercentToGridLengthConverter_50Percent_ReturnsGridLength50Star()
    {
        // Arrange
        var converter = PercentToGridLengthConverter.Instance;

        // Act
        var result = converter.Convert(50.0, typeof(GridLength), null, CultureInfo.InvariantCulture);

        // Assert
        result.Should().BeOfType<GridLength>();
        var gridLength = (GridLength)result!;
        gridLength.Value.Should().Be(50.0);
        gridLength.GridUnitType.Should().Be(GridUnitType.Star);
    }

    [Test]
    public void PercentToGridLengthConverter_0Percent_ReturnsGridLength0Star()
    {
        // Arrange
        var converter = PercentToGridLengthConverter.Instance;

        // Act
        var result = converter.Convert(0.0, typeof(GridLength), null, CultureInfo.InvariantCulture);

        // Assert
        result.Should().BeOfType<GridLength>();
        var gridLength = (GridLength)result!;
        gridLength.Value.Should().Be(0.0);
        gridLength.GridUnitType.Should().Be(GridUnitType.Star);
    }

    [Test]
    public void PercentToGridLengthConverter_100Percent_ReturnsGridLength100Star()
    {
        // Arrange
        var converter = PercentToGridLengthConverter.Instance;

        // Act
        var result = converter.Convert(100.0, typeof(GridLength), null, CultureInfo.InvariantCulture);

        // Assert
        result.Should().BeOfType<GridLength>();
        var gridLength = (GridLength)result!;
        gridLength.Value.Should().Be(100.0);
        gridLength.GridUnitType.Should().Be(GridUnitType.Star);
    }

    [Test]
    public void PercentToGridLengthConverter_NegativeValue_ClampsToZero()
    {
        // Arrange
        var converter = PercentToGridLengthConverter.Instance;

        // Act
        var result = converter.Convert(-10.0, typeof(GridLength), null, CultureInfo.InvariantCulture);

        // Assert
        result.Should().BeOfType<GridLength>();
        var gridLength = (GridLength)result!;
        gridLength.Value.Should().Be(0.0);
        gridLength.GridUnitType.Should().Be(GridUnitType.Star);
    }

    [Test]
    public void PercentToGridLengthConverter_Over100_ClampsTo100()
    {
        // Arrange
        var converter = PercentToGridLengthConverter.Instance;

        // Act
        var result = converter.Convert(150.0, typeof(GridLength), null, CultureInfo.InvariantCulture);

        // Assert
        result.Should().BeOfType<GridLength>();
        var gridLength = (GridLength)result!;
        gridLength.Value.Should().Be(100.0);
        gridLength.GridUnitType.Should().Be(GridUnitType.Star);
    }

    [Test]
    public void PercentToGridLengthConverter_NonDouble_ReturnsGridLength0Star()
    {
        // Arrange
        var converter = PercentToGridLengthConverter.Instance;

        // Act
        var result = converter.Convert("not a number", typeof(GridLength), null, CultureInfo.InvariantCulture);

        // Assert
        result.Should().BeOfType<GridLength>();
        var gridLength = (GridLength)result!;
        gridLength.Value.Should().Be(0.0);
        gridLength.GridUnitType.Should().Be(GridUnitType.Star);
    }

    [Test]
    public void PercentToGridLengthConverter_ConvertBack_ReturnsDoNothing()
    {
        // Arrange
        var converter = PercentToGridLengthConverter.Instance;

        // Act
        var result = converter.ConvertBack(new GridLength(50, GridUnitType.Star), typeof(double), null, CultureInfo.InvariantCulture);

        // Assert
        result.Should().Be(global::Avalonia.Data.BindingOperations.DoNothing);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // PercentToRemainingGridLengthConverter
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void PercentToRemainingGridLengthConverter_30Percent_ReturnsGridLength70Star()
    {
        // Arrange
        var converter = PercentToRemainingGridLengthConverter.Instance;

        // Act
        var result = converter.Convert(30.0, typeof(GridLength), null, CultureInfo.InvariantCulture);

        // Assert
        result.Should().BeOfType<GridLength>();
        var gridLength = (GridLength)result!;
        gridLength.Value.Should().Be(70.0);
        gridLength.GridUnitType.Should().Be(GridUnitType.Star);
    }

    [Test]
    public void PercentToRemainingGridLengthConverter_0Percent_ReturnsGridLength100Star()
    {
        // Arrange
        var converter = PercentToRemainingGridLengthConverter.Instance;

        // Act
        var result = converter.Convert(0.0, typeof(GridLength), null, CultureInfo.InvariantCulture);

        // Assert
        result.Should().BeOfType<GridLength>();
        var gridLength = (GridLength)result!;
        gridLength.Value.Should().Be(100.0);
        gridLength.GridUnitType.Should().Be(GridUnitType.Star);
    }

    [Test]
    public void PercentToRemainingGridLengthConverter_100Percent_ReturnsGridLength0Star()
    {
        // Arrange
        var converter = PercentToRemainingGridLengthConverter.Instance;

        // Act
        var result = converter.Convert(100.0, typeof(GridLength), null, CultureInfo.InvariantCulture);

        // Assert
        result.Should().BeOfType<GridLength>();
        var gridLength = (GridLength)result!;
        gridLength.Value.Should().Be(0.0);
        gridLength.GridUnitType.Should().Be(GridUnitType.Star);
    }

    [Test]
    public void PercentToRemainingGridLengthConverter_NonDouble_ReturnsGridLength100Star()
    {
        // Arrange
        var converter = PercentToRemainingGridLengthConverter.Instance;

        // Act
        var result = converter.Convert(null, typeof(GridLength), null, CultureInfo.InvariantCulture);

        // Assert
        result.Should().BeOfType<GridLength>();
        var gridLength = (GridLength)result!;
        gridLength.Value.Should().Be(100.0);
        gridLength.GridUnitType.Should().Be(GridUnitType.Star);
    }

    [Test]
    public void PercentToRemainingGridLengthConverter_ConvertBack_ReturnsDoNothing()
    {
        // Arrange
        var converter = PercentToRemainingGridLengthConverter.Instance;

        // Act
        var result = converter.ConvertBack(new GridLength(70, GridUnitType.Star), typeof(double), null, CultureInfo.InvariantCulture);

        // Assert
        result.Should().Be(global::Avalonia.Data.BindingOperations.DoNothing);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // PercentToWidthConverter
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void PercentToWidthConverter_50Percent_Returns50PercentString()
    {
        // Arrange
        var converter = PercentToWidthConverter.Instance;

        // Act
        var result = converter.Convert(50.0, typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        result.Should().Be("50%");
    }

    [Test]
    public void PercentToWidthConverter_0Percent_Returns0PercentString()
    {
        // Arrange
        var converter = PercentToWidthConverter.Instance;

        // Act
        var result = converter.Convert(0.0, typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        result.Should().Be("0%");
    }

    [Test]
    public void PercentToWidthConverter_100Percent_Returns100PercentString()
    {
        // Arrange
        var converter = PercentToWidthConverter.Instance;

        // Act
        var result = converter.Convert(100.0, typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        result.Should().Be("100%");
    }

    [Test]
    public void PercentToWidthConverter_NegativeValue_ClampsToZero()
    {
        // Arrange
        var converter = PercentToWidthConverter.Instance;

        // Act
        var result = converter.Convert(-20.0, typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        result.Should().Be("0%");
    }

    [Test]
    public void PercentToWidthConverter_Over100_ClampsTo100()
    {
        // Arrange
        var converter = PercentToWidthConverter.Instance;

        // Act
        var result = converter.Convert(200.0, typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        result.Should().Be("100%");
    }

    [Test]
    public void PercentToWidthConverter_NonDouble_Returns0Percent()
    {
        // Arrange
        var converter = PercentToWidthConverter.Instance;

        // Act
        var result = converter.Convert("not a number", typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        result.Should().Be("0%");
    }

    [Test]
    public void PercentToWidthConverter_ConvertBack_ReturnsDoNothing()
    {
        // Arrange
        var converter = PercentToWidthConverter.Instance;

        // Act
        var result = converter.ConvertBack("50%", typeof(double), null, CultureInfo.InvariantCulture);

        // Assert
        result.Should().Be(global::Avalonia.Data.BindingOperations.DoNothing);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SelectedRowBorderConverter
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void SelectedRowBorderConverter_MatchingDates_ReturnsNonTransparent()
    {
        // Arrange
        var converter = SelectedRowBorderConverter.Instance;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var values = new List<object?> { (DateOnly?)today, (DateOnly?)today };

        // Act
        var result = converter.Convert(values, typeof(IBrush), null, CultureInfo.InvariantCulture);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeAssignableTo<IBrush>();
        result.Should().NotBeSameAs(Brushes.Transparent);
    }

    [Test]
    public void SelectedRowBorderConverter_NonMatchingDates_ReturnsTransparent()
    {
        // Arrange
        var converter = SelectedRowBorderConverter.Instance;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var yesterday = today.AddDays(-1);
        var values = new List<object?> { (DateOnly?)today, (DateOnly?)yesterday };

        // Act
        var result = converter.Convert(values, typeof(IBrush), null, CultureInfo.InvariantCulture);

        // Assert
        result.Should().BeSameAs(Brushes.Transparent);
    }
}
