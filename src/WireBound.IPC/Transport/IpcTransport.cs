using System.IO;
using System.Text;
using System.Text.Json;
using WireBound.IPC.Messages;

namespace WireBound.IPC.Transport;

/// <summary>
/// Handles sending and receiving IPC messages over a stream (pipe or socket).
/// Uses length-prefixed JSON framing: [4-byte length][JSON payload]
/// </summary>
public static class IpcTransport
{
    public static async Task SendAsync(Stream stream, IpcMessage message, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);
        var length = BitConverter.GetBytes(bytes.Length);

        await stream.WriteAsync(length, ct);
        await stream.WriteAsync(bytes, ct);
        await stream.FlushAsync(ct);
    }

    public static async Task<IpcMessage?> ReceiveAsync(Stream stream, CancellationToken ct = default)
    {
        var lengthBuffer = new byte[4];
        var bytesRead = await ReadExactAsync(stream, lengthBuffer, ct);
        if (bytesRead < 4) return null;

        var length = BitConverter.ToInt32(lengthBuffer, 0);
        if (length <= 0 || length > 1_048_576) return null; // Max 1MB

        var buffer = new byte[length];
        bytesRead = await ReadExactAsync(stream, buffer, ct);
        if (bytesRead < length) return null;

        var json = Encoding.UTF8.GetString(buffer);
        return JsonSerializer.Deserialize<IpcMessage>(json);
    }

    private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), ct);
            if (read == 0) return totalRead;
            totalRead += read;
        }
        return totalRead;
    }
}
