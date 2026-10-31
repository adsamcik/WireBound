using System.Runtime.Versioning;
using WireBound.Platform.Abstract.Services;
using WireBound.Platform.Windows.Services;

namespace WireBound.Tests.Platform;

// These tests exercise launch policy through delegates; no Windows task or UAC is invoked.
[SupportedOSPlatform("windows")]
public class WindowsHelperStartupTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task InvalidRegistration_NeverRunsTask_OnlyExplicitStartMayPrompt(bool interactive)
    {
        var scheduledCalls = 0;
        var interactiveCalls = 0;
        var result = await WindowsHelperProcessManager.StartRegisteredHelperAsync(
            HelperValidationResult.Invalid("Old executable path"), interactive,
            () => { scheduledCalls++; return Task.FromResult(HelperStartResult.Success(1)); },
            () => { interactiveCalls++; return Task.FromResult(HelperStartResult.Success(2)); });

        scheduledCalls.Should().Be(0);
        interactiveCalls.Should().Be(interactive ? 1 : 0);
        result.Status.Should().Be(interactive ? HelperStartStatus.Success : HelperStartStatus.ValidationFailed);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ValidRegistration_StartsTaskWithoutPrompt(bool interactive)
    {
        var promptCalls = 0;
        var result = await WindowsHelperProcessManager.StartRegisteredHelperAsync(
            HelperValidationResult.Valid(), interactive,
            () => Task.FromResult(HelperStartResult.Success(12)),
            () => { promptCalls++; return Task.FromResult(HelperStartResult.Success(13)); });

        result.ProcessId.Should().Be(12);
        promptCalls.Should().Be(0);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task FailedScheduledStart_PromptsOnlyForExplicitStart(bool interactive)
    {
        var promptCalls = 0;
        var result = await WindowsHelperProcessManager.StartRegisteredHelperAsync(
            HelperValidationResult.Valid(), interactive,
            () => Task.FromResult(HelperStartResult.Failed("Task unavailable")),
            () => { promptCalls++; return Task.FromResult(HelperStartResult.Cancelled()); });

        promptCalls.Should().Be(interactive ? 1 : 0);
        result.Status.Should().Be(interactive ? HelperStartStatus.UserCancelled : HelperStartStatus.Failed);
    }
}
