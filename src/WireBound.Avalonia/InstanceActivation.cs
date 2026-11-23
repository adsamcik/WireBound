using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Serilog;

namespace WireBound.Avalonia;

internal static class InstanceActivation
{
    private const PipeOptions Options = PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly;
    private const byte ShowWindow = 1;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(2);

    internal static string GetPipeName(string dataDirectory)
    {
        if (OperatingSystem.IsWindows())
        {
            // The mutex is local to the Windows session. Named pipes are machine-wide.
            using var process = Process.GetCurrentProcess();
            dataDirectory = $"{process.SessionId}:{dataDirectory.ToUpperInvariant()}";
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(dataDirectory));
        var name = $"WireBound-Activate-{Convert.ToHexString(hash.AsSpan(0, 12))}";
        // Use a stable, short Unix socket path even when TMPDIR differs between launches.
        return OperatingSystem.IsWindows() ? name : $"/tmp/{name}";
    }

    internal static async Task<bool> RequestAsync(string pipeName)
    {
        try
        {
            using var timeout = new CancellationTokenSource(RequestTimeout);
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, Options,
                TokenImpersonationLevel.Identification);
            await client.ConnectAsync(timeout.Token).ConfigureAwait(false);

            if (OperatingSystem.IsWindows())
                GrantForegroundAccess(client.SafePipeHandle);

            var message = new[] { ShowWindow };
            await client.WriteAsync(message, timeout.Token).ConfigureAwait(false);
            // Wait for the server to consume the request before closing our handle.
            return await client.ReadAsync(message, timeout.Token).ConfigureAwait(false) == 0;
        }
        catch (Exception ex) when (ex is IOException or SocketException or UnauthorizedAccessException or OperationCanceledException)
        {
            return false;
        }
    }

    internal static async Task ListenAsync(string pipeName, Task<Action> windowActivation, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Windows can retain the previous instance briefly while its client closes.
                bool showWindow;
                using (var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, Options))
                {
                    showWindow = await ReadRequestAsync(server, cancellationToken).ConfigureAwait(false);
                }
                if (!showWindow)
                    continue;

                // A launch received during startup opens the window after the tray preference is applied.
                var activate = await windowActivation.WaitAsync(cancellationToken).ConfigureAwait(false);
                activate();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is IOException or SocketException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Could not listen for WireBound window activation requests");
        }
    }

    private static async Task<bool> ReadRequestAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        try
        {
            await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);
            var message = new byte[1];
            return await server.ReadAsync(message, timeout.Token).ConfigureAwait(false) == 1 && message[0] == ShowWindow;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex) when (ex is IOException or SocketException or UnauthorizedAccessException)
        {
            // A disconnected or rejected peer must not disable later relaunches.
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static void GrantForegroundAccess(SafePipeHandle pipe)
    {
        if (GetNamedPipeServerProcessId(pipe, out var processId) && processId != 0)
            _ = AllowSetForegroundWindow(processId);
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
