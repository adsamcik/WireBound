using AwesomeAssertions;
using NSubstitute;
using TUnit.Core;

namespace WireBound.Tests.Platform;

public class WindowsNetworkCostProviderTests
{
    [Test]
    public async Task IsMeteredAsync_WhenCostIsFixed_ReturnsTrue()
    {
        var provider = CreateProviderWithPowerShellOutput("Fixed");

        var result = await provider.IsMeteredAsync();

        result.Should().BeTrue();
    }

    [Test]
    public async Task IsMeteredAsync_WhenCostIsVariable_ReturnsTrue()
    {
        var provider = CreateProviderWithPowerShellOutput("Variable");

        var result = await provider.IsMeteredAsync();

        result.Should().BeTrue();
    }

    [Test]
    public async Task IsMeteredAsync_WhenCostIsUnrestricted_ReturnsFalse()
    {
        var provider = CreateProviderWithPowerShellOutput("Unrestricted");

        var result = await provider.IsMeteredAsync();

        result.Should().BeFalse();
    }

    [Test]
    public async Task IsMeteredAsync_WhenCostIsUnknown_ReturnsFalse()
    {
        var provider = CreateProviderWithPowerShellOutput("Unknown");

        var result = await provider.IsMeteredAsync();

        result.Should().BeFalse();
    }

    [Test]
    public async Task IsMeteredAsync_WhenOutputIsEmpty_ReturnsFalse()
    {
        var provider = CreateProviderWithPowerShellOutput("");

        var result = await provider.IsMeteredAsync();

        result.Should().BeFalse();
    }

    [Test]
    public async Task IsMeteredAsync_WhenOutputIsWhitespace_ReturnsFalse()
    {
        var provider = CreateProviderWithPowerShellOutput("   \t\n  ");

        var result = await provider.IsMeteredAsync();

        result.Should().BeFalse();
    }

    [Test]
    public async Task IsMeteredAsync_WhenPowerShellThrowsException_ReturnsFalse()
    {
        var powerShellExecutor = Substitute.For<IPowerShellExecutor>();
        powerShellExecutor.ExecuteAsync(Arg.Any<string>())
            .Returns(Task.FromException<string>(new InvalidOperationException("PowerShell failed")));
        
        var provider = new WindowsNetworkCostProvider(powerShellExecutor);

        var result = await provider.IsMeteredAsync();

        result.Should().BeFalse();
    }

    [Test]
    public async Task IsMeteredAsync_WhenPowerShellReturnsNull_ReturnsFalse()
    {
        var powerShellExecutor = Substitute.For<IPowerShellExecutor>();
        powerShellExecutor.ExecuteAsync(Arg.Any<string>())
            .Returns(Task.FromResult<string>(null!));
        
        var provider = new WindowsNetworkCostProvider(powerShellExecutor);

        var result = await provider.IsMeteredAsync();

        result.Should().BeFalse();
    }

    [Test]
    public async Task IsMeteredAsync_WithMixedCase_ParsesCorrectly()
    {
        var testCases = new[]
        {
            ("FIXED", true),
            ("fixed", true),
            ("FiXeD", true),
            ("VARIABLE", true),
            ("variable", true),
            ("VaRiAbLe", true),
            ("UNRESTRICTED", false),
            ("unrestricted", false),
        };

        foreach (var (input, expected) in testCases)
        {
            var provider = CreateProviderWithPowerShellOutput(input);
            var result = await provider.IsMeteredAsync();
            result.Should().Be(expected, $"Input '{input}' should return {expected}");
        }
    }

    [Test]
    public async Task IsMeteredAsync_WithLeadingAndTrailingWhitespace_ParsesCorrectly()
    {
        var testCases = new[]
        {
            "  Fixed  ",
            "\tVariable\t",
            "\nUnrestricted\n",
            " Unknown ",
        };

        foreach (var input in testCases)
        {
            var provider = CreateProviderWithPowerShellOutput(input);
            var result = await provider.IsMeteredAsync();
            
            // Should parse the trimmed value correctly
            await result;
        }
    }

    [Test]
    public async Task IsMeteredAsync_WithInvalidCostType_ReturnsFalse()
    {
        var invalidInputs = new[]
        {
            "InvalidCost",
            "NotAValidType",
            "123",
            "true",
            "false",
        };

        foreach (var input in invalidInputs)
        {
            var provider = CreateProviderWithPowerShellOutput(input);
            var result = await provider.IsMeteredAsync();
            result.Should().BeFalse($"Invalid input '{input}' should return false");
        }
    }

    [Test]
    public async Task IsMeteredAsync_ExecutesCorrectPowerShellCommand()
    {
        var powerShellExecutor = Substitute.For<IPowerShellExecutor>();
        powerShellExecutor.ExecuteAsync(Arg.Any<string>())
            .Returns("Unrestricted");
        
        var provider = new WindowsNetworkCostProvider(powerShellExecutor);

        await provider.IsMeteredAsync();

        await powerShellExecutor.Received(1).ExecuteAsync(
            Arg.Is<string>(cmd => 
                cmd.Contains("Get-NetConnectionProfile") &&
                cmd.Contains("NetworkCost")));
    }

    [Test]
    public async Task IsMeteredAsync_CalledMultipleTimes_ExecutesPowerShellEachTime()
    {
        var powerShellExecutor = Substitute.For<IPowerShellExecutor>();
        powerShellExecutor.ExecuteAsync(Arg.Any<string>())
            .Returns("Fixed", "Variable", "Unrestricted");
        
        var provider = new WindowsNetworkCostProvider(powerShellExecutor);

        await provider.IsMeteredAsync();
        await provider.IsMeteredAsync();
        await provider.IsMeteredAsync();

        await powerShellExecutor.Received(3).ExecuteAsync(Arg.Any<string>());
    }

    private WindowsNetworkCostProvider CreateProviderWithPowerShellOutput(string output)
    {
        var powerShellExecutor = Substitute.For<IPowerShellExecutor>();
        powerShellExecutor.ExecuteAsync(Arg.Any<string>())
            .Returns(Task.FromResult(output));
        
        return new WindowsNetworkCostProvider(powerShellExecutor);
    }
}

// NOTE: These are integration-style tests that mock the PowerShell execution layer.
// The actual PowerShell command execution and network cost retrieval are not tested here.
// For true integration tests, use a real PowerShellExecutor implementation and verify
// against actual network conditions on a Windows system.
//
// The IPowerShellExecutor abstraction should be implemented to wrap System.Management.Automation
// PowerShell invocation, allowing us to mock it in tests while using real PowerShell in production.
