namespace WireBound.IPC.Transport;

/// <summary>
/// Thrown by <see cref="IpcTransport"/> when the length-prefixed framing layer
/// detects an unrecoverable condition (short read, invalid length, MessagePack
/// decode failure, framing-level timeout). The stream pointer may now sit
/// mid-frame, so the only safe recovery is for the caller to dispose the pipe
/// and reconnect — there is no way to resynchronize a length-prefixed binary
/// stream after even a single missing byte.
/// </summary>
public sealed class IpcFramingException : Exception
{
    public IpcFramingException(string message) : base(message) { }
    public IpcFramingException(string message, Exception innerException) : base(message, innerException) { }
}
