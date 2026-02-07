using WireBound.Avalonia.Services;

namespace WireBound.Tests.Services;

/// <summary>
/// Unit tests for LocalizationService
/// </summary>
public class LocalizationServiceTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // GetString Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void GetString_KnownKey_ReturnsValue()
    {
        // Arrange
        var service = new LocalizationService();

        // Act
        var result = service.GetString("Dashboard_Title");

        // Assert
        result.Should().Be("Dashboard");
    }

    [Test]
    public void GetString_UnknownKey_ReturnsKey()
    {
        // Arrange
        var service = new LocalizationService();

        // Act
        var result = service.GetString("NonExistent_Key");

        // Assert
        result.Should().Be("NonExistent_Key");
    }

    [Test]
    [Arguments("Settings_Title", "Settings")]
    [Arguments("Charts_Title", "Live Chart")]
    [Arguments("Applications_Title", "Applications")]
    public void GetString_KnownKeys_ReturnCorrectValues(string key, string expected)
    {
        // Arrange
        var service = new LocalizationService();

        // Act
        var result = service.GetString(key);

        // Assert
        result.Should().Be(expected);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GetString with Format Args Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void GetString_WithFormatArgs_FormatsCorrectly()
    {
        // Arrange
        var service = new LocalizationService();
        // Unknown key returns the key itself as template
        var result = service.GetString("Hello {0}!", "World");

        // Assert - key not found, so template is "Hello {0}!", formatted with "World"
        result.Should().Be("Hello World!");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CurrentCulture Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void CurrentCulture_ReturnsNonNull()
    {
        // Arrange
        var service = new LocalizationService();

        // Act
        var culture = service.CurrentCulture;

        // Assert
        culture.Should().NotBeNull();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Strings Static Class Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void Strings_AfterInitialize_ReturnsValues()
    {
        // Arrange
        var service = new LocalizationService();
        Strings.Initialize(service);

        // Act
        var result = Strings.Get("Dashboard_Title");

        // Assert
        result.Should().Be("Dashboard");
    }

    [Test]
    public void Strings_AfterInitialize_UnknownKey_ReturnsKey()
    {
        // Arrange
        var service = new LocalizationService();
        Strings.Initialize(service);

        // Act
        var result = Strings.Get("Unknown_Key");

        // Assert
        result.Should().Be("Unknown_Key");
    }
}
