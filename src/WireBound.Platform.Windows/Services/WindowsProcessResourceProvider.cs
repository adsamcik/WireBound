using System.Diagnostics;
using System.Runtime.Versioning;
using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Windows.Services;

/// <summary>
/// Windows implementation of per-process resource data provider.
/// Uses Process.GetProcesses() for a single-pass enumeration of CPU + memory.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsProcessResourceProvider : IProcessResourceProvider
{
    public Task<IReadOnlyList<ProcessResourceData>> GetProcessResourceDataAsync(
        CancellationToken cancellationToken = default)
    {
        var results = new List<ProcessResourceData>();

        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch
        {
            return Task.FromResult<IReadOnlyList<ProcessResourceData>>(results);
        }

        foreach (var process in processes)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                string exePath = string.Empty;
                try
                {
                    exePath = process.MainModule?.FileName ?? string.Empty;
                }
                catch
                {
                    // Access denied for system processes — expected
                }

                results.Add(new ProcessResourceData
                {
                    ProcessId = process.Id,
                    ProcessName = process.ProcessName,
                    ExecutablePath = exePath,
                    PrivateBytes = process.PrivateMemorySize64,
                    WorkingSetBytes = process.WorkingSet64,
                    CpuTimeTicks = process.TotalProcessorTime.Ticks
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Process may have exited between enumeration and access — skip
            }
            finally
            {
                process.Dispose();
            }
        }

        return Task.FromResult<IReadOnlyList<ProcessResourceData>>(results);
    }
}
