using WireBound.Models;

namespace WireBound.Tests.Models;

/// <summary>
/// Tests for AppSettings model
/// </summary>
public class AppSettingsTests
{
    #region Default Values Tests

    [Test]
    public async Task AppSettings_DefaultId_IsOne()
    {
        // Arrange & Act
        var settings = new AppSettings();

        // Assert - Singleton pattern: default ID is 1
        await Assert.That(settings.Id).IsEqualTo(1);
    }

    [Test]
    public async Task AppSettings_DefaultPollingInterval_IsOneSecond()
    {
        // Arrange & Act
        var settings = new AppSettings();

        // Assert
        await Assert.That(settings.PollingIntervalMs).IsEqualTo(1000);
    }

    [Test]
    public async Task AppSettings_DefaultSaveInterval_IsSixtySeconds()
    {
        // Arrange & Act
        var settings = new AppSettings();

        // Assert
        await Assert.That(settings.SaveIntervalSeconds).IsEqualTo(60);
    }

    [Test]
    public async Task AppSettings_DefaultStartWithWindows_IsFalse()
    {
        // Arrange & Act
        var settings = new AppSettings();

        // Assert
        await Assert.That(settings.StartWithWindows).IsFalse();
    }

    [Test]
    public async Task AppSettings_DefaultMinimizeToTray_IsTrue()
    {
        // Arrange & Act
        var settings = new AppSettings();

        // Assert
        await Assert.That(settings.MinimizeToTray).IsTrue();
    }

    [Test]
    public async Task AppSettings_DefaultUseIpHelperApi_IsFalse()
    {
        // Arrange & Act
        var settings = new AppSettings();

        // Assert
        await Assert.That(settings.UseIpHelperApi).IsFalse();
    }

    [Test]
    public async Task AppSettings_DefaultSelectedAdapterId_IsEmpty()
    {
        // Arrange & Act
        var settings = new AppSettings();

        // Assert
        await Assert.That(settings.SelectedAdapterId).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task AppSettings_DefaultDataRetentionDays_IsOneYear()
    {
        // Arrange & Act
        var settings = new AppSettings();

        // Assert
        await Assert.That(settings.DataRetentionDays).IsEqualTo(365);
    }

    #endregion

    #region Property Assignment Tests

    [Test]
    public async Task AppSettings_PollingInterval_CanBeModified()
    {
        // Arrange
        var settings = new AppSettings();

        // Act
        settings.PollingIntervalMs = 500; // 500ms for faster updates

        // Assert
        await Assert.That(settings.PollingIntervalMs).IsEqualTo(500);
    }

    [Test]
    public async Task AppSettings_SaveInterval_CanBeModified()
    {
        // Arrange
        var settings = new AppSettings();

        // Act
        settings.SaveIntervalSeconds = 30; // Save every 30 seconds

        // Assert
        await Assert.That(settings.SaveIntervalSeconds).IsEqualTo(30);
    }

    [Test]
    public async Task AppSettings_StartWithWindows_CanBeEnabled()
    {
        // Arrange
        var settings = new AppSettings();

        // Act
        settings.StartWithWindows = true;

        // Assert
        await Assert.That(settings.StartWithWindows).IsTrue();
    }

    [Test]
    public async Task AppSettings_MinimizeToTray_CanBeDisabled()
    {
        // Arrange
        var settings = new AppSettings();

        // Act
        settings.MinimizeToTray = false;

        // Assert
        await Assert.That(settings.MinimizeToTray).IsFalse();
    }

    [Test]
    public async Task AppSettings_UseIpHelperApi_CanBeEnabled()
    {
        // Arrange
        var settings = new AppSettings();

        // Act
        settings.UseIpHelperApi = true;

        // Assert
        await Assert.That(settings.UseIpHelperApi).IsTrue();
    }

    [Test]
    public async Task AppSettings_SelectedAdapterId_CanBeSet()
    {
        // Arrange
        var settings = new AppSettings();
        var adapterId = "{12345678-ABCD-EF00-1234-567890ABCDEF}";

        // Act
        settings.SelectedAdapterId = adapterId;

        // Assert
        await Assert.That(settings.SelectedAdapterId).IsEqualTo(adapterId);
    }

    [Test]
    public async Task AppSettings_DataRetentionDays_CanBeModified()
    {
        // Arrange
        var settings = new AppSettings();

        // Act
        settings.DataRetentionDays = 30; // Keep only 30 days

        // Assert
        await Assert.That(settings.DataRetentionDays).IsEqualTo(30);
    }

    #endregion

    #region Validation Boundary Tests

    [Test]
    public async Task AppSettings_PollingInterval_MinimumReasonableValue()
    {
        // Arrange - Very fast polling (100ms)
        var settings = new AppSettings { PollingIntervalMs = 100 };

        // Assert
        await Assert.That(settings.PollingIntervalMs).IsEqualTo(100);
    }

    [Test]
    public async Task AppSettings_PollingInterval_SlowValue()
    {
        // Arrange - Slow polling (5 seconds)
        var settings = new AppSettings { PollingIntervalMs = 5000 };

        // Assert
        await Assert.That(settings.PollingIntervalMs).IsEqualTo(5000);
    }

    [Test]
    public async Task AppSettings_DataRetentionDays_MinimumValue()
    {
        // Arrange - Keep only 1 day
        var settings = new AppSettings { DataRetentionDays = 1 };

        // Assert
        await Assert.That(settings.DataRetentionDays).IsEqualTo(1);
    }

    [Test]
    public async Task AppSettings_DataRetentionDays_LargeValue()
    {
        // Arrange - Keep 10 years of data
        var settings = new AppSettings { DataRetentionDays = 3650 };

        // Assert
        await Assert.That(settings.DataRetentionDays).IsEqualTo(3650);
    }

    #endregion

    #region Complete Configuration Tests

    [Test]
    public async Task AppSettings_FullConfiguration_CanBeSet()
    {
        // Arrange & Act
        var settings = new AppSettings
        {
            Id = 1,
            PollingIntervalMs = 500,
            SaveIntervalSeconds = 30,
            StartWithWindows = true,
            MinimizeToTray = true,
            UseIpHelperApi = false,
            SelectedAdapterId = "eth0",
            DataRetentionDays = 180
        };

        // Assert
        await Assert.That(settings.Id).IsEqualTo(1);
        await Assert.That(settings.PollingIntervalMs).IsEqualTo(500);
        await Assert.That(settings.SaveIntervalSeconds).IsEqualTo(30);
        await Assert.That(settings.StartWithWindows).IsTrue();
        await Assert.That(settings.MinimizeToTray).IsTrue();
        await Assert.That(settings.UseIpHelperApi).IsFalse();
        await Assert.That(settings.SelectedAdapterId).IsEqualTo("eth0");
        await Assert.That(settings.DataRetentionDays).IsEqualTo(180);
    }

    #endregion
}
