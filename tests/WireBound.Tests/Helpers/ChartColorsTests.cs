using AwesomeAssertions;
using SkiaSharp;
using WireBound.Core.Helpers;

namespace WireBound.Tests.Helpers;

/// <summary>
/// Unit tests for ChartColors helper class
/// </summary>
public class ChartColorsTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // NETWORK ACTIVITY COLORS
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void DownloadColor_ShouldReturnValidElectricCyan()
    {
        // Act
        var color = ChartColors.DownloadColor;

        // Assert
        color.Should().NotBe(SKColor.Empty);
        color.Red.Should().Be(0);
        color.Green.Should().Be(229);
        color.Blue.Should().Be(255);
        color.Alpha.Should().Be(255);
    }

    [Test]
    public void DownloadAccentColor_ShouldReturnValidColor()
    {
        // Act
        var color = ChartColors.DownloadAccentColor;

        // Assert
        color.Should().NotBe(SKColor.Empty);
        color.Alpha.Should().Be(255);
    }

    [Test]
    public void DownloadDimColor_ShouldReturnValidColor()
    {
        // Act
        var color = ChartColors.DownloadDimColor;

        // Assert
        color.Should().NotBe(SKColor.Empty);
        color.Alpha.Should().Be(255);
    }

    [Test]
    public void UploadColor_ShouldReturnValidCoralOrange()
    {
        // Act
        var color = ChartColors.UploadColor;

        // Assert
        color.Should().NotBe(SKColor.Empty);
        color.Red.Should().Be(255);
        color.Green.Should().Be(107);
        color.Blue.Should().Be(53);
        color.Alpha.Should().Be(255);
    }

    [Test]
    public void UploadAccentColor_ShouldReturnValidColor()
    {
        // Act
        var color = ChartColors.UploadAccentColor;

        // Assert
        color.Should().NotBe(SKColor.Empty);
        color.Alpha.Should().Be(255);
    }

    [Test]
    public void UploadDimColor_ShouldReturnValidColor()
    {
        // Act
        var color = ChartColors.UploadDimColor;

        // Assert
        color.Should().NotBe(SKColor.Empty);
        color.Alpha.Should().Be(255);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SYSTEM RESOURCE COLORS
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void CpuColor_ShouldReturnValidSapphireBlue()
    {
        // Act
        var color = ChartColors.CpuColor;

        // Assert
        color.Should().NotBe(SKColor.Empty);
        color.Red.Should().Be(59);
        color.Green.Should().Be(130);
        color.Blue.Should().Be(246);
        color.Alpha.Should().Be(255);
    }

    [Test]
    public void CpuAccentColor_ShouldReturnValidColor()
    {
        // Act
        var color = ChartColors.CpuAccentColor;

        // Assert
        color.Should().NotBe(SKColor.Empty);
        color.Alpha.Should().Be(255);
    }

    [Test]
    public void CpuDimColor_ShouldReturnValidColor()
    {
        // Act
        var color = ChartColors.CpuDimColor;

        // Assert
        color.Should().NotBe(SKColor.Empty);
        color.Alpha.Should().Be(255);
    }

    [Test]
    public void MemoryColor_ShouldReturnValidAmethystPurple()
    {
        // Act
        var color = ChartColors.MemoryColor;

        // Assert
        color.Should().NotBe(SKColor.Empty);
        color.Red.Should().Be(168);
        color.Green.Should().Be(85);
        color.Blue.Should().Be(247);
        color.Alpha.Should().Be(255);
    }

    [Test]
    public void MemoryAccentColor_ShouldReturnValidColor()
    {
        // Act
        var color = ChartColors.MemoryAccentColor;

        // Assert
        color.Should().NotBe(SKColor.Empty);
        color.Alpha.Should().Be(255);
    }

    [Test]
    public void MemoryDimColor_ShouldReturnValidColor()
    {
        // Act
        var color = ChartColors.MemoryDimColor;

        // Assert
        color.Should().NotBe(SKColor.Empty);
        color.Alpha.Should().Be(255);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CHART AXIS & GRID COLORS
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void AxisLabelColor_ShouldReturnValidColor()
    {
        // Act
        var color = ChartColors.AxisLabelColor;

        // Assert
        color.Should().NotBe(SKColor.Empty);
        color.Alpha.Should().Be(255);
    }

    [Test]
    public void AxisNameColor_ShouldReturnValidColor()
    {
        // Act
        var color = ChartColors.AxisNameColor;

        // Assert
        color.Should().NotBe(SKColor.Empty);
        color.Alpha.Should().Be(255);
    }

    [Test]
    public void GridLineColor_ShouldHaveAlphaTransparency()
    {
        // Act
        var color = ChartColors.GridLineColor;

        // Assert
        color.Should().NotBe(SKColor.Empty);
        color.Alpha.Should().Be(100);  // Partial transparency
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SECTION & THRESHOLD COLORS
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void SectionStrokeColor_ShouldHaveAlphaTransparency()
    {
        // Act
        var color = ChartColors.SectionStrokeColor;

        // Assert
        color.Should().NotBe(SKColor.Empty);
        color.Alpha.Should().Be(150);
    }

    [Test]
    public void WarningSectionColor_ShouldHaveAlphaTransparency()
    {
        // Act
        var color = ChartColors.WarningSectionColor;

        // Assert
        color.Should().NotBe(SKColor.Empty);
        color.Alpha.Should().Be(60);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // BACKGROUND & TOOLTIP COLORS
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ChartBackgroundColor_ShouldReturnValidColor()
    {
        // Act
        var color = ChartColors.ChartBackgroundColor;

        // Assert
        color.Should().NotBe(SKColor.Empty);
        color.Alpha.Should().Be(255);
    }

    [Test]
    public void TooltipBackgroundColor_ShouldReturnValidColor()
    {
        // Act
        var color = ChartColors.TooltipBackgroundColor;

        // Assert
        color.Should().NotBe(SKColor.Empty);
        color.Alpha.Should().Be(255);
    }

    [Test]
    public void TooltipTextColor_ShouldReturnValidColor()
    {
        // Act
        var color = ChartColors.TooltipTextColor;

        // Assert
        color.Should().NotBe(SKColor.Empty);
        color.Alpha.Should().Be(255);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SERIES PALETTE
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void SeriesPalette_ShouldContainMultipleColors()
    {
        // Act
        var palette = ChartColors.SeriesPalette;

        // Assert
        palette.Should().NotBeNull();
        palette.Should().HaveCountGreaterThanOrEqualTo(8);
    }

    [Test]
    public void SeriesPalette_AllColorsShouldBeValid()
    {
        // Act
        var palette = ChartColors.SeriesPalette;

        // Assert
        foreach (var color in palette)
        {
            color.Should().NotBe(SKColor.Empty);
            color.Alpha.Should().Be(255);
        }
    }

    [Test]
    public void SeriesPalette_FirstColorShouldBeElectricCyan()
    {
        // Act
        var firstColor = ChartColors.SeriesPalette[0];

        // Assert
        firstColor.Red.Should().Be(0);
        firstColor.Green.Should().Be(229);
        firstColor.Blue.Should().Be(255);
    }

    [Test]
    public void SeriesPalette_SecondColorShouldBeCoralOrange()
    {
        // Act
        var secondColor = ChartColors.SeriesPalette[1];

        // Assert
        secondColor.Red.Should().Be(255);
        secondColor.Green.Should().Be(107);
        secondColor.Blue.Should().Be(53);
    }

    [Test]
    public void SeriesPalette_ColorsShouldBeDistinct()
    {
        // Act
        var palette = ChartColors.SeriesPalette;

        // Assert
        palette.Should().OnlyHaveUniqueItems();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // COLOR RELATIONSHIP TESTS
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void DownloadColors_ShouldBeInCyanFamily()
    {
        // All download colors should have higher blue component
        ChartColors.DownloadColor.Blue.Should().BeGreaterThan(ChartColors.DownloadColor.Red);
        ChartColors.DownloadAccentColor.Blue.Should().BeGreaterThan(ChartColors.DownloadAccentColor.Red);
        ChartColors.DownloadDimColor.Blue.Should().BeGreaterThan(ChartColors.DownloadDimColor.Red);
    }

    [Test]
    public void UploadColors_ShouldBeInOrangeFamily()
    {
        // All upload colors should have higher red component
        ChartColors.UploadColor.Red.Should().BeGreaterThan(ChartColors.UploadColor.Blue);
        ChartColors.UploadAccentColor.Red.Should().BeGreaterThan(ChartColors.UploadAccentColor.Blue);
        ChartColors.UploadDimColor.Red.Should().BeGreaterThan(ChartColors.UploadDimColor.Blue);
    }

    [Test]
    public void CpuColors_ShouldBeInBlueFamily()
    {
        // CPU colors should have dominant blue component
        ChartColors.CpuColor.Blue.Should().BeGreaterThan(ChartColors.CpuColor.Red);
        ChartColors.CpuColor.Blue.Should().BeGreaterThan(ChartColors.CpuColor.Green);
    }

    [Test]
    public void MemoryColors_ShouldBeInPurpleFamily()
    {
        // Memory colors should have high red and blue (purple)
        ChartColors.MemoryColor.Blue.Should().BeGreaterThan(ChartColors.MemoryColor.Green);
        ChartColors.MemoryColor.Red.Should().BeGreaterThan(ChartColors.MemoryColor.Green);
    }
}
