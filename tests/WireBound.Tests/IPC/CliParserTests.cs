using System.Runtime.Versioning;
using WireBound.Elevation.Windows;

namespace WireBound.Tests.IPC;

/// <summary>
/// Tests for the Windows elevation CLI argument parser.
/// </summary>
[SupportedOSPlatform("windows")]
public class CliParserTests
{
    [Test]
    public void ParseCallerSid_WithValidArgs_ReturnsSid()
    {
        var args = new[] { "--caller-sid", "S-1-5-21-1234567890-1234567890-1234567890-1001" };
        var result = CliParser.ParseCallerSid(args);
        result.Should().Be("S-1-5-21-1234567890-1234567890-1234567890-1001");
    }

    [Test]
    public void ParseCallerSid_CaseInsensitive()
    {
        var args = new[] { "--CALLER-SID", "S-1-5-21-123" };
        CliParser.ParseCallerSid(args).Should().Be("S-1-5-21-123");

        args = ["--Caller-Sid", "S-1-5-21-456"];
        CliParser.ParseCallerSid(args).Should().Be("S-1-5-21-456");
    }

    [Test]
    public void ParseCallerSid_NoArgs_ReturnsNull()
    {
        CliParser.ParseCallerSid([]).Should().BeNull();
    }

    [Test]
    public void ParseCallerSid_MissingValue_ReturnsNull()
    {
        // --caller-sid is the last arg, no value follows
        var args = new[] { "--caller-sid" };
        CliParser.ParseCallerSid(args).Should().BeNull();
    }

    [Test]
    public void ParseCallerSid_OtherArgs_ReturnsNull()
    {
        var args = new[] { "--verbose", "--log-level", "debug" };
        CliParser.ParseCallerSid(args).Should().BeNull();
    }

    [Test]
    public void ParseCallerSid_MixedWithOtherArgs_ReturnsSid()
    {
        var args = new[] { "--verbose", "--caller-sid", "S-1-5-21-999", "--log-level", "debug" };
        CliParser.ParseCallerSid(args).Should().Be("S-1-5-21-999");
    }

    [Test]
    public void ParseCallerSid_MultipleOccurrences_ReturnsFirst()
    {
        var args = new[] { "--caller-sid", "first", "--caller-sid", "second" };
        CliParser.ParseCallerSid(args).Should().Be("first");
    }

    [Test]
    public void ParseCallerSid_EmptyStringValue_ReturnsEmptyString()
    {
        var args = new[] { "--caller-sid", "" };
        CliParser.ParseCallerSid(args).Should().Be("");
    }

    [Test]
    public void ParseCallerSid_SingleArg_ReturnsNull()
    {
        // Only one arg, loop condition i < args.Length - 1 prevents out of bounds
        var args = new[] { "something" };
        CliParser.ParseCallerSid(args).Should().BeNull();
    }
}
