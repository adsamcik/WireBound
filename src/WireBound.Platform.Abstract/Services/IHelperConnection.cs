namespace WireBound.Platform.Abstract.Services;

/// <summary>
/// Abstraction for IPC connection to the elevated helper process.
/// Handles named pipes (Windows) or Unix sockets (Linux).
/// </summary>
public interface IHelperConnection : IAsyncDisposable
{
    /// <summary>
    /// Whether the connection to the helper is currently established.
    /// </summary>
    bool IsConnected { get; }
    
    /// <summary>
    /// Connect to the elevated helper process.
    /// May launch the helper if it's not running.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if connection established successfully</returns>
    Task<bool> ConnectAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Disconnect from the helper process.
    /// </summary>
    Task DisconnectAsync();
    
    /// <summary>
    /// Send a request to the helper and receive a response.
    /// </summary>
    /// <typeparam name="TRequest">Request type</typeparam>
    /// <typeparam name="TResponse">Response type</typeparam>
    /// <param name="request">The request to send</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The response from the helper</returns>
    Task<TResponse> SendRequestAsync<TRequest, TResponse>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class;
    
    /// <summary>
    /// Event raised when the connection to the helper is lost.
    /// </summary>
    event EventHandler<HelperConnectionLostEventArgs>? ConnectionLost;
}

/// <summary>
/// Event args for helper connection lost events.
/// </summary>
public class HelperConnectionLostEventArgs : EventArgs
{
    /// <summary>
    /// Reason the connection was lost.
    /// </summary>
    public string Reason { get; }
    
    /// <summary>
    /// Whether automatic reconnection will be attempted.
    /// </summary>
    public bool WillReconnect { get; }
    
    public HelperConnectionLostEventArgs(string reason, bool willReconnect)
    {
        Reason = reason;
        WillReconnect = willReconnect;
    }
}
