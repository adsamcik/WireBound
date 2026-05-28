using System.IO.Pipes;
using System.Reflection;
using System.Runtime.Versioning;
using WireBound.IPC.Messages;
using WireBound.IPC.Transport;
using WireBound.Platform.Windows.Services;

namespace WireBound.Tests.IPC;

[SupportedOSPlatform("windows")]
public class WindowsHelperConnectionConcurrencyTests
{
    [Test]
    public async Task IsConnected_WhenFaulted_ReturnsFalse()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var server = CreateServer(out var pipeName);
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await using var connection = new WindowsHelperConnection();
        await ConnectPairAsync(server, client);
        SetPrivateField(connection, "_pipe", client);

        connection.IsConnected.Should().BeTrue();

        SetPrivateField(connection, "_faulted", true);

        connection.IsConnected.Should().BeFalse();
    }

    [Test]
    public async Task SendRequestAsync_ConcurrentRequests_CompletesWithoutFramingErrors()
    {
        if (!OperatingSystem.IsWindows())
            return;

        const int requestCount = 20;
        await using var server = CreateServer(out var pipeName);
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await using var connection = new WindowsHelperConnection();
        await ConnectPairAsync(server, client);
        SetPrivateField(connection, "_pipe", client);

        var serverTask = Task.Run(async () =>
        {
            for (var i = 0; i < requestCount; i++)
            {
                var request = await IpcTransport.ReceiveAsync(server, timeout: Timeout.InfiniteTimeSpan);
                var response = new IpcMessage
                {
                    Type = MessageType.Heartbeat,
                    RequestId = request.RequestId,
                    Payload = IpcTransport.SerializePayload(new HeartbeatResponse { Alive = true })
                };

                await IpcTransport.SendAsync(server, response);
            }
        });

        var tasks = Enumerable.Range(0, requestCount)
            .Select(i => connection.SendRequestAsync<HeartbeatRequest, HeartbeatResponse>(
                new HeartbeatRequest { SessionId = $"session-{i}" }))
            .ToArray();

        var responses = await Task.WhenAll(tasks);
        await serverTask;

        responses.Should().OnlyContain(response => response.Alive);
    }

    [Test]
    public async Task SendRequestAsync_RequestIdMismatch_ThrowsInvalidOperationExceptionAndFaultsConnection()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var server = CreateServer(out var pipeName);
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await using var connection = new WindowsHelperConnection();
        await ConnectPairAsync(server, client);
        SetPrivateField(connection, "_pipe", client);

        var serverTask = Task.Run(async () =>
        {
            var request = await IpcTransport.ReceiveAsync(server, timeout: Timeout.InfiniteTimeSpan);
            var response = new IpcMessage
            {
                Type = MessageType.Heartbeat,
                RequestId = $"{request.RequestId}-mismatch",
                Payload = IpcTransport.SerializePayload(new HeartbeatResponse { Alive = true })
            };

            await IpcTransport.SendAsync(server, response);
        });

        Func<Task> act = async () => await connection.SendRequestAsync<HeartbeatRequest, HeartbeatResponse>(
            new HeartbeatRequest { SessionId = "session" });

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("RequestId mismatch");
        await serverTask;
        connection.IsConnected.Should().BeFalse();
    }

    private static NamedPipeServerStream CreateServer(out string pipeName)
    {
        pipeName = $"WireBound.Tests.{Guid.NewGuid():N}";
        return new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
    }

    private static async Task ConnectPairAsync(NamedPipeServerStream server, NamedPipeClientStream client)
    {
        var serverConnect = server.WaitForConnectionAsync();
        await client.ConnectAsync(1000);
        await serverConnect;
    }

    private static void SetPrivateField<T>(WindowsHelperConnection connection, string fieldName, T value)
    {
        var field = typeof(WindowsHelperConnection).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull($"{fieldName} should exist for the IPC hardening smoke test");
        field!.SetValue(connection, value);
    }
}
