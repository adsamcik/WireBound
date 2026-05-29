using System.Runtime.Versioning;
using WireBound.IPC.Messages;
using WireBound.Platform.Abstract.Services;
using WireBound.Platform.Windows.Services;
using WireBound.Tests.Fixtures;

namespace WireBound.Tests.Platform;

[WindowsOnly]
[SupportedOSPlatform("windows")]
public class WindowsElevatedProcessNetworkProviderErrorSurfacingTests
{
    [Test]
    public async Task GetProcessStatsAsync_HelperReturnsFailure_RaisesErrorOccurred()
    {
        var (provider, connection) = CreateProvider();
        connection.SendRequestAsync<ProcessStatsRequest, ProcessStatsResponse>(
                Arg.Any<ProcessStatsRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(new ProcessStatsResponse { Success = false, ErrorMessage = "ETW failed" });
        string? errorMessage = null;
        provider.ErrorOccurred += (_, args) => errorMessage = args.Message;

        var result = await provider.GetProcessStatsAsync();

        result.Should().BeEmpty();
        errorMessage.Should().Contain("ETW failed");
    }

    [Test]
    public async Task GetActiveConnectionsAsync_HelperReturnsFailure_RaisesErrorOccurred()
    {
        var (provider, connection) = CreateProvider();
        connection.SendRequestAsync<ConnectionStatsRequest, ConnectionStatsResponse>(
                Arg.Any<ConnectionStatsRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(new ConnectionStatsResponse { Success = false, ErrorMessage = "Access denied" });
        string? errorMessage = null;
        provider.ErrorOccurred += (_, args) => errorMessage = args.Message;

        var result = await provider.GetActiveConnectionsAsync();

        result.Should().BeEmpty();
        errorMessage.Should().Contain("Access denied");
    }

    [Test]
    public async Task GetConnectionStatsAsync_HelperReturnsFailure_RaisesErrorOccurred()
    {
        var (provider, connection) = CreateProvider();
        connection.SendRequestAsync<ConnectionStatsRequest, ConnectionStatsResponse>(
                Arg.Any<ConnectionStatsRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(new ConnectionStatsResponse { Success = false, ErrorMessage = "ETW session failed" });
        string? errorMessage = null;
        provider.ErrorOccurred += (_, args) => errorMessage = args.Message;

        var result = await provider.GetConnectionStatsAsync();

        result.Should().BeEmpty();
        errorMessage.Should().Contain("ETW session failed");
    }

    [Test]
    public async Task GetProcessStatsAsync_HelperReturnsFailureWithNoMessage_RaisesGenericErrorOccurred()
    {
        var (provider, connection) = CreateProvider();
        connection.SendRequestAsync<ProcessStatsRequest, ProcessStatsResponse>(
                Arg.Any<ProcessStatsRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(new ProcessStatsResponse { Success = false, ErrorMessage = string.Empty });
        string? errorMessage = null;
        provider.ErrorOccurred += (_, args) => errorMessage = args.Message;

        var result = await provider.GetProcessStatsAsync();

        result.Should().BeEmpty();
        errorMessage.Should().Contain("ProcessStatsResponse");
        errorMessage.Should().Contain("no error message");
    }

    [Test]
    public async Task DisposeAsync_AwaitsMonitoringTask()
    {
        var (provider, connection) = CreateProvider();
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRequest = new TaskCompletionSource<ProcessStatsResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.SendRequestAsync<ProcessStatsRequest, ProcessStatsResponse>(
                Arg.Any<ProcessStatsRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                requestStarted.TrySetResult();
                return releaseRequest.Task;
            });

        await provider.StartMonitoringAsync();
        // PollAsync's first iteration sleeps ~2s before issuing a request, so
        // give the test deadline generous headroom — under heavy parallel test
        // load the Task.Delay can be substantially longer than 2s.
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(15));

        var disposeTask = provider.DisposeAsync().AsTask();
        await Task.Delay(100);
        disposeTask.IsCompleted.Should().BeFalse();

        releaseRequest.SetResult(new ProcessStatsResponse { Success = true });
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static (WindowsElevatedProcessNetworkProvider Provider, IHelperConnection Connection) CreateProvider()
    {
        var connection = Substitute.For<IHelperConnection>();
        connection.IsConnected.Returns(true);
        return (new WindowsElevatedProcessNetworkProvider(connection), connection);
    }
}
