using AwesomeAssertions;
using WireBound.Core.Models;

namespace WireBound.Tests.Models;

/// <summary>
/// Unit tests for HourlySystemStats and DailySystemStats model classes
/// </summary>
public class HourlySystemStatsTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // HourlySystemStats - Default Values
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void HourlySystemStats_NewInstance_HasDefaultValues()
    {
        // Act
        var stats = new HourlySystemStats();

        // Assert
        stats.Id.Should().Be(0);
        stats.Hour.Should().Be(default);
        stats.AvgCpuPercent.Should().Be(0);
        stats.MaxCpuPercent.Should().Be(0);
        stats.MinCpuPercent.Should().Be(0);
        stats.AvgMemoryPercent.Should().Be(0);
        stats.MaxMemoryPercent.Should().Be(0);
        stats.AvgMemoryUsedBytes.Should().Be(0);
    }

    [Fact]
    public void HourlySystemStats_NewInstance_GpuPropertiesAreNull()
    {
        // Act
        var stats = new HourlySystemStats();

        // Assert
        stats.AvgGpuPercent.Should().BeNull();
        stats.MaxGpuPercent.Should().BeNull();
        stats.AvgGpuMemoryPercent.Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // HourlySystemStats - Property Setters/Getters
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void HourlySystemStats_Id_CanBeSetAndRetrieved()
    {
        // Arrange
        var stats = new HourlySystemStats();

        // Act
        stats.Id = 42;

        // Assert
        stats.Id.Should().Be(42);
    }

    [Fact]
    public void HourlySystemStats_Hour_CanBeSetAndRetrieved()
    {
        // Arrange
        var stats = new HourlySystemStats();
        var expectedHour = new DateTime(2026, 1, 24, 14, 0, 0);

        // Act
        stats.Hour = expectedHour;

        // Assert
        stats.Hour.Should().Be(expectedHour);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(50.5)]
    [InlineData(100.0)]
    public void HourlySystemStats_AvgCpuPercent_CanBeSetAndRetrieved(double value)
    {
        // Arrange
        var stats = new HourlySystemStats();

        // Act
        stats.AvgCpuPercent = value;

        // Assert
        stats.AvgCpuPercent.Should().Be(value);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(75.25)]
    [InlineData(100.0)]
    public void HourlySystemStats_MaxCpuPercent_CanBeSetAndRetrieved(double value)
    {
        // Arrange
        var stats = new HourlySystemStats();

        // Act
        stats.MaxCpuPercent = value;

        // Assert
        stats.MaxCpuPercent.Should().Be(value);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(10.5)]
    [InlineData(100.0)]
    public void HourlySystemStats_MinCpuPercent_CanBeSetAndRetrieved(double value)
    {
        // Arrange
        var stats = new HourlySystemStats();

        // Act
        stats.MinCpuPercent = value;

        // Assert
        stats.MinCpuPercent.Should().Be(value);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(65.75)]
    [InlineData(100.0)]
    public void HourlySystemStats_AvgMemoryPercent_CanBeSetAndRetrieved(double value)
    {
        // Arrange
        var stats = new HourlySystemStats();

        // Act
        stats.AvgMemoryPercent = value;

        // Assert
        stats.AvgMemoryPercent.Should().Be(value);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(85.5)]
    [InlineData(100.0)]
    public void HourlySystemStats_MaxMemoryPercent_CanBeSetAndRetrieved(double value)
    {
        // Arrange
        var stats = new HourlySystemStats();

        // Act
        stats.MaxMemoryPercent = value;

        // Assert
        stats.MaxMemoryPercent.Should().Be(value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8589934592)]  // 8 GB
    [InlineData(17179869184)] // 16 GB
    public void HourlySystemStats_AvgMemoryUsedBytes_CanBeSetAndRetrieved(long value)
    {
        // Arrange
        var stats = new HourlySystemStats();

        // Act
        stats.AvgMemoryUsedBytes = value;

        // Assert
        stats.AvgMemoryUsedBytes.Should().Be(value);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // HourlySystemStats - Nullable GPU Properties
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void HourlySystemStats_AvgGpuPercent_CanBeNull()
    {
        // Arrange
        var stats = new HourlySystemStats();

        // Act
        stats.AvgGpuPercent = null;

        // Assert
        stats.AvgGpuPercent.Should().BeNull();
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(45.5)]
    [InlineData(100.0)]
    public void HourlySystemStats_AvgGpuPercent_CanBeSetToValue(double value)
    {
        // Arrange
        var stats = new HourlySystemStats();

        // Act
        stats.AvgGpuPercent = value;

        // Assert
        stats.AvgGpuPercent.Should().Be(value);
    }

    [Fact]
    public void HourlySystemStats_MaxGpuPercent_CanBeNull()
    {
        // Arrange
        var stats = new HourlySystemStats();

        // Act
        stats.MaxGpuPercent = null;

        // Assert
        stats.MaxGpuPercent.Should().BeNull();
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(95.75)]
    [InlineData(100.0)]
    public void HourlySystemStats_MaxGpuPercent_CanBeSetToValue(double value)
    {
        // Arrange
        var stats = new HourlySystemStats();

        // Act
        stats.MaxGpuPercent = value;

        // Assert
        stats.MaxGpuPercent.Should().Be(value);
    }

    [Fact]
    public void HourlySystemStats_AvgGpuMemoryPercent_CanBeNull()
    {
        // Arrange
        var stats = new HourlySystemStats();

        // Act
        stats.AvgGpuMemoryPercent = null;

        // Assert
        stats.AvgGpuMemoryPercent.Should().BeNull();
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(60.25)]
    [InlineData(100.0)]
    public void HourlySystemStats_AvgGpuMemoryPercent_CanBeSetToValue(double value)
    {
        // Arrange
        var stats = new HourlySystemStats();

        // Act
        stats.AvgGpuMemoryPercent = value;

        // Assert
        stats.AvgGpuMemoryPercent.Should().Be(value);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // HourlySystemStats - Complete Object Test
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void HourlySystemStats_AllPropertiesSet_ReturnsCorrectValues()
    {
        // Arrange
        var stats = new HourlySystemStats
        {
            Id = 1,
            Hour = new DateTime(2026, 1, 24, 14, 0, 0),
            AvgCpuPercent = 35.5,
            MaxCpuPercent = 75.0,
            MinCpuPercent = 5.0,
            AvgMemoryPercent = 60.0,
            MaxMemoryPercent = 80.0,
            AvgMemoryUsedBytes = 8589934592,
            AvgGpuPercent = 25.0,
            MaxGpuPercent = 50.0,
            AvgGpuMemoryPercent = 40.0
        };

        // Assert
        stats.Id.Should().Be(1);
        stats.Hour.Should().Be(new DateTime(2026, 1, 24, 14, 0, 0));
        stats.AvgCpuPercent.Should().Be(35.5);
        stats.MaxCpuPercent.Should().Be(75.0);
        stats.MinCpuPercent.Should().Be(5.0);
        stats.AvgMemoryPercent.Should().Be(60.0);
        stats.MaxMemoryPercent.Should().Be(80.0);
        stats.AvgMemoryUsedBytes.Should().Be(8589934592);
        stats.AvgGpuPercent.Should().Be(25.0);
        stats.MaxGpuPercent.Should().Be(50.0);
        stats.AvgGpuMemoryPercent.Should().Be(40.0);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // DailySystemStats - Default Values
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void DailySystemStats_NewInstance_HasDefaultValues()
    {
        // Act
        var stats = new DailySystemStats();

        // Assert
        stats.Id.Should().Be(0);
        stats.Date.Should().Be(default);
        stats.AvgCpuPercent.Should().Be(0);
        stats.MaxCpuPercent.Should().Be(0);
        stats.AvgMemoryPercent.Should().Be(0);
        stats.MaxMemoryPercent.Should().Be(0);
        stats.PeakMemoryUsedBytes.Should().Be(0);
    }

    [Fact]
    public void DailySystemStats_NewInstance_GpuPropertiesAreNull()
    {
        // Act
        var stats = new DailySystemStats();

        // Assert
        stats.AvgGpuPercent.Should().BeNull();
        stats.MaxGpuPercent.Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // DailySystemStats - Property Setters/Getters
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void DailySystemStats_Id_CanBeSetAndRetrieved()
    {
        // Arrange
        var stats = new DailySystemStats();

        // Act
        stats.Id = 100;

        // Assert
        stats.Id.Should().Be(100);
    }

    [Fact]
    public void DailySystemStats_Date_CanBeSetAndRetrieved()
    {
        // Arrange
        var stats = new DailySystemStats();
        var expectedDate = new DateOnly(2026, 1, 24);

        // Act
        stats.Date = expectedDate;

        // Assert
        stats.Date.Should().Be(expectedDate);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(42.5)]
    [InlineData(100.0)]
    public void DailySystemStats_AvgCpuPercent_CanBeSetAndRetrieved(double value)
    {
        // Arrange
        var stats = new DailySystemStats();

        // Act
        stats.AvgCpuPercent = value;

        // Assert
        stats.AvgCpuPercent.Should().Be(value);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(88.75)]
    [InlineData(100.0)]
    public void DailySystemStats_MaxCpuPercent_CanBeSetAndRetrieved(double value)
    {
        // Arrange
        var stats = new DailySystemStats();

        // Act
        stats.MaxCpuPercent = value;

        // Assert
        stats.MaxCpuPercent.Should().Be(value);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(55.25)]
    [InlineData(100.0)]
    public void DailySystemStats_AvgMemoryPercent_CanBeSetAndRetrieved(double value)
    {
        // Arrange
        var stats = new DailySystemStats();

        // Act
        stats.AvgMemoryPercent = value;

        // Assert
        stats.AvgMemoryPercent.Should().Be(value);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(90.5)]
    [InlineData(100.0)]
    public void DailySystemStats_MaxMemoryPercent_CanBeSetAndRetrieved(double value)
    {
        // Arrange
        var stats = new DailySystemStats();

        // Act
        stats.MaxMemoryPercent = value;

        // Assert
        stats.MaxMemoryPercent.Should().Be(value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16106127360)]  // 15 GB
    [InlineData(34359738368)]  // 32 GB
    public void DailySystemStats_PeakMemoryUsedBytes_CanBeSetAndRetrieved(long value)
    {
        // Arrange
        var stats = new DailySystemStats();

        // Act
        stats.PeakMemoryUsedBytes = value;

        // Assert
        stats.PeakMemoryUsedBytes.Should().Be(value);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // DailySystemStats - Nullable GPU Properties
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void DailySystemStats_AvgGpuPercent_CanBeNull()
    {
        // Arrange
        var stats = new DailySystemStats();

        // Act
        stats.AvgGpuPercent = null;

        // Assert
        stats.AvgGpuPercent.Should().BeNull();
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(30.0)]
    [InlineData(100.0)]
    public void DailySystemStats_AvgGpuPercent_CanBeSetToValue(double value)
    {
        // Arrange
        var stats = new DailySystemStats();

        // Act
        stats.AvgGpuPercent = value;

        // Assert
        stats.AvgGpuPercent.Should().Be(value);
    }

    [Fact]
    public void DailySystemStats_MaxGpuPercent_CanBeNull()
    {
        // Arrange
        var stats = new DailySystemStats();

        // Act
        stats.MaxGpuPercent = null;

        // Assert
        stats.MaxGpuPercent.Should().BeNull();
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(85.0)]
    [InlineData(100.0)]
    public void DailySystemStats_MaxGpuPercent_CanBeSetToValue(double value)
    {
        // Arrange
        var stats = new DailySystemStats();

        // Act
        stats.MaxGpuPercent = value;

        // Assert
        stats.MaxGpuPercent.Should().Be(value);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // DailySystemStats - Complete Object Test
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void DailySystemStats_AllPropertiesSet_ReturnsCorrectValues()
    {
        // Arrange
        var stats = new DailySystemStats
        {
            Id = 1,
            Date = new DateOnly(2026, 1, 24),
            AvgCpuPercent = 40.0,
            MaxCpuPercent = 95.0,
            AvgMemoryPercent = 65.5,
            MaxMemoryPercent = 85.0,
            PeakMemoryUsedBytes = 17179869184,
            AvgGpuPercent = 35.0,
            MaxGpuPercent = 70.0
        };

        // Assert
        stats.Id.Should().Be(1);
        stats.Date.Should().Be(new DateOnly(2026, 1, 24));
        stats.AvgCpuPercent.Should().Be(40.0);
        stats.MaxCpuPercent.Should().Be(95.0);
        stats.AvgMemoryPercent.Should().Be(65.5);
        stats.MaxMemoryPercent.Should().Be(85.0);
        stats.PeakMemoryUsedBytes.Should().Be(17179869184);
        stats.AvgGpuPercent.Should().Be(35.0);
        stats.MaxGpuPercent.Should().Be(70.0);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Cross-Model Comparison Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void HourlyAndDailyStats_HaveComparableProperties()
    {
        // This test ensures both models have the expected shared properties

        // Arrange
        var hourly = new HourlySystemStats();
        var daily = new DailySystemStats();

        // Both should have Id
        hourly.Id.Should().Be(daily.Id);

        // Both should have CPU metrics
        hourly.AvgCpuPercent.Should().Be(daily.AvgCpuPercent);
        hourly.MaxCpuPercent.Should().Be(daily.MaxCpuPercent);

        // Both should have Memory metrics
        hourly.AvgMemoryPercent.Should().Be(daily.AvgMemoryPercent);
        hourly.MaxMemoryPercent.Should().Be(daily.MaxMemoryPercent);

        // Both should have nullable GPU metrics
        hourly.AvgGpuPercent.Should().Be(daily.AvgGpuPercent);
        hourly.MaxGpuPercent.Should().Be(daily.MaxGpuPercent);
    }
}
