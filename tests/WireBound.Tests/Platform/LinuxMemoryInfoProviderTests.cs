#pragma warning disable CA1416

using WireBound.Platform.Linux.Services;

namespace WireBound.Tests.Platform;

public class LinuxMemoryInfoProviderTests
{
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // Standard Parsing Tests
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    [Test]
    public void ParseMemInfoLines_StandardMemInfo_ParsesAllCommonKeys()
    {
        // Arrange
        var lines = new[]
        {
            "MemTotal:       16384000 kB",
            "MemFree:         1234567 kB",
            "MemAvailable:    8765432 kB",
            "Buffers:          345678 kB",
            "Cached:          4567890 kB",
            "SwapTotal:       8192000 kB",
            "SwapFree:        7654321 kB"
        };

        // Act
        var result = LinuxMemoryInfoProvider.ParseMemInfoLines(lines);

        // Assert
        result.Should().HaveCount(7);
        result["MemTotal"].Should().Be(16384000L);
        result["MemFree"].Should().Be(1234567L);
        result["MemAvailable"].Should().Be(8765432L);
        result["Buffers"].Should().Be(345678L);
        result["Cached"].Should().Be(4567890L);
        result["SwapTotal"].Should().Be(8192000L);
        result["SwapFree"].Should().Be(7654321L);
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // Empty / Missing Input Tests
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    [Test]
    public void ParseMemInfoLines_EmptyLines_ReturnsEmptyDictionary()
    {
        // Arrange
        var lines = Array.Empty<string>();

        // Act
        var result = LinuxMemoryInfoProvider.ParseMemInfoLines(lines);

        // Assert
        result.Should().BeEmpty();
    }

    [Test]
    public void ParseMemInfoLines_LinesWithoutColon_AreSkipped()
    {
        // Arrange
        var lines = new[]
        {
            "This line has no colon",
            "Neither does this one",
            ""
        };

        // Act
        var result = LinuxMemoryInfoProvider.ParseMemInfoLines(lines);

        // Assert
        result.Should().BeEmpty();
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // Value Format Tests
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    [Test]
    public void ParseMemInfoLines_ValuesWithoutKbSuffix_StillParse()
    {
        // Arrange
        var lines = new[]
        {
            "HugePages_Total:       0",
            "HugePages_Free:        0",
            "HugePages_Surp:        5"
        };

        // Act
        var result = LinuxMemoryInfoProvider.ParseMemInfoLines(lines);

        // Assert
        result.Should().HaveCount(3);
        result["HugePages_Total"].Should().Be(0L);
        result["HugePages_Free"].Should().Be(0L);
        result["HugePages_Surp"].Should().Be(5L);
    }

    [Test]
    public void ParseMemInfoLines_LargeValues_ParsesCorrectly()
    {
        // Arrange ÔÇö 128 GB system
        var lines = new[]
        {
            "MemTotal:       134217728 kB",
            "MemFree:         67108864 kB",
            "SwapTotal:       67108864 kB"
        };

        // Act
        var result = LinuxMemoryInfoProvider.ParseMemInfoLines(lines);

        // Assert
        result["MemTotal"].Should().Be(134217728L);
        result["MemFree"].Should().Be(67108864L);
        result["SwapTotal"].Should().Be(67108864L);
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // Case Sensitivity Tests
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    [Test]
    public void ParseMemInfoLines_KeyLookup_IsCaseInsensitive()
    {
        // Arrange
        var lines = new[]
        {
            "MemTotal:       16384000 kB"
        };

        // Act
        var result = LinuxMemoryInfoProvider.ParseMemInfoLines(lines);

        // Assert
        result["memtotal"].Should().Be(16384000L);
        result["MEMTOTAL"].Should().Be(16384000L);
        result["MemTotal"].Should().Be(16384000L);
        result["memTotal"].Should().Be(16384000L);
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // Malformed Input Tests
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    [Test]
    public void ParseMemInfoLines_MalformedNonNumericValue_IsSkipped()
    {
        // Arrange
        var lines = new[]
        {
            "MemTotal:       abc kB",
            "MemFree:        1234567 kB"
        };

        // Act
        var result = LinuxMemoryInfoProvider.ParseMemInfoLines(lines);

        // Assert
        result.Should().HaveCount(1);
        result.Should().ContainKey("MemFree");
        result.Should().NotContainKey("MemTotal");
    }

    [Test]
    public void ParseMemInfoLines_MultipleSpacesBetweenColonAndValue_ParsesCorrectly()
    {
        // Arrange
        var lines = new[]
        {
            "MemTotal:                    16384000 kB",
            "MemFree:  1234567 kB"
        };

        // Act
        var result = LinuxMemoryInfoProvider.ParseMemInfoLines(lines);

        // Assert
        result["MemTotal"].Should().Be(16384000L);
        result["MemFree"].Should().Be(1234567L);
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // Single Entry / Edge Case Tests
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    [Test]
    public void ParseMemInfoLines_SingleKeyValuePair_ParsesCorrectly()
    {
        // Arrange
        var lines = new[]
        {
            "MemTotal:       16384000 kB"
        };

        // Act
        var result = LinuxMemoryInfoProvider.ParseMemInfoLines(lines);

        // Assert
        result.Should().HaveCount(1);
        result["MemTotal"].Should().Be(16384000L);
    }

    [Test]
    public void ParseMemInfoLines_KeysWithSpecialCharacters_ParsesCorrectly()
    {
        // Arrange ÔÇö real /proc/meminfo keys contain underscores and parens
        var lines = new[]
        {
            "DirectMap4k:      123456 kB",
            "DirectMap2M:      654321 kB",
            "HugePages_Total:       0",
            "VmallocTotal:   34359738367 kB"
        };

        // Act
        var result = LinuxMemoryInfoProvider.ParseMemInfoLines(lines);

        // Assert
        result.Should().HaveCount(4);
        result["DirectMap4k"].Should().Be(123456L);
        result["DirectMap2M"].Should().Be(654321L);
        result["HugePages_Total"].Should().Be(0L);
        result["VmallocTotal"].Should().Be(34359738367L);
    }

    [Test]
    public void ParseMemInfoLines_MixedValidAndInvalidLines_ParsesOnlyValid()
    {
        // Arrange
        var lines = new[]
        {
            "MemTotal:       16384000 kB",
            "invalid line without colon",
            "",
            "MemFree:        notanumber kB",
            "Cached:          4567890 kB"
        };

        // Act
        var result = LinuxMemoryInfoProvider.ParseMemInfoLines(lines);

        // Assert
        result.Should().HaveCount(2);
        result["MemTotal"].Should().Be(16384000L);
        result["Cached"].Should().Be(4567890L);
    }

    [Test]
    public void ParseMemInfoLines_ColonAtStartOfLine_IsSkipped()
    {
        // Arrange ÔÇö colonIndex would be 0, which should be skipped
        var lines = new[]
        {
            ":       16384000 kB"
        };

        // Act
        var result = LinuxMemoryInfoProvider.ParseMemInfoLines(lines);

        // Assert
        result.Should().BeEmpty();
    }
}
