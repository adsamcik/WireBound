using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Stub.Services;

/// <summary>
/// Stub implementation of <see cref="IHelperConnection"/> for unsupported platforms.
/// </summary>
public sealed class StubHelperConnection : IHelperConnection
{
    public bool IsConnected => false;

#pragma warning disable CS0067 // Event is never used (stub implementation)
    public event EventHandler<HelperConnectionLostEventArgs>? ConnectionLost;
#pragma warning restore CS0067

    public Task<bool> ConnectAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task DisconnectAsync() =>
        Task.CompletedTask;

    public Task<TResponse> SendRequestAsync<TRequest, TResponse>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class =>
        Task.FromException<TResponse>(new NotSupportedException("Helper connection is not supported on this platform"));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
