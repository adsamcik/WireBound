using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace WireBound.Platform.Abstract.Helpers;

/// <summary>Noninteractive, bounded, read-only CLI queries. Arguments never pass through a shell.</summary>
public static class LocalCommandRunner
{
    public static async Task<string?> RunAsync(string executable, IEnumerable<string> arguments,
        CancellationToken cancellationToken, int timeoutSeconds = 5, bool detectUtf16 = false,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8
            }
        };
        process.StartInfo.Environment["LC_ALL"] = "C";
        if (environment is not null)
            foreach (var (name, value) in environment)
                if (value is null) process.StartInfo.Environment.Remove(name);
                else process.StartInfo.Environment[name] = value;
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        try
        {
            if (!process.Start()) return null;
            // Drain both pipes concurrently, including on failure, to avoid pipe deadlocks.
            var output = ReadOutputAsync(process.StandardOutput, detectUtf16, deadline.Token);
            var error = process.StandardError.ReadToEndAsync(deadline.Token);
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            await error.ConfigureAwait(false);
            var text = await output.ConfigureAwait(false);
            return process.ExitCode == 0 ? text : null;
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { }
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
        catch (Win32Exception) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (IOException) { return null; }
    }

    private static async Task<string> ReadOutputAsync(StreamReader reader, bool detectUtf16, CancellationToken token)
    {
        if (!detectUtf16) return await reader.ReadToEndAsync(token).ConfigureAwait(false);
        using var bytes = new MemoryStream();
        await reader.BaseStream.CopyToAsync(bytes, token).ConfigureAwait(false);
        return DecodeOutput(bytes.ToArray());
    }

    internal static string DecodeOutput(byte[] data)
    {
        // WSL --list has shipped both UTF-8 and UTF-16LE, independent of WSL_UTF8.
        // A valid distribution name cannot contain NUL; UTF-16 line endings do.
        var encoding = data.Contains((byte)0) || (data.Length >= 2 && data[0] == 0xff && data[1] == 0xfe)
            ? Encoding.Unicode : Encoding.UTF8;
        return encoding.GetString(data).TrimStart('\uFEFF');
    }
}
