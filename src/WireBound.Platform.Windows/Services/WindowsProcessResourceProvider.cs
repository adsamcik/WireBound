using System.Diagnostics;
using System.Runtime.Versioning;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
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
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryCounters
    {
        public uint Size, PageFaultCount;
        public nuint PeakWorkingSet, WorkingSet, PeakPagedPool, PagedPool,
            PeakNonpagedPool, NonpagedPool, Pagefile, PeakPagefile, PrivateUsage, PrivateWorkingSet;
        public ulong SharedCommit;
    }

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessMemoryInfo(SafeProcessHandle handle, ref MemoryCounters counters, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    private static long? ReadPrivateWorkingSet(Process process)
    {
        // Older Windows versions may accept the larger buffer without filling EX2 fields.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22631)) return null;
        try
        {
            var counters = new MemoryCounters { Size = (uint)Marshal.SizeOf<MemoryCounters>() };
            using var handle = OpenProcess(0x1000, false, process.Id); // PROCESS_QUERY_LIMITED_INFORMATION
            return !handle.IsInvalid && GetProcessMemoryInfo(handle, ref counters, counters.Size)
                && counters.PrivateWorkingSet <= long.MaxValue ? (long)counters.PrivateWorkingSet : null;
        }
        catch (System.ComponentModel.Win32Exception) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    public Task<IReadOnlyList<ProcessResourceData>> GetProcessResourceDataAsync(
        CancellationToken cancellationToken = default, bool includePrivateWorkingSet = false)
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
                long startMarker = 0;
                try
                {
                    startMarker = process.StartTime.ToUniversalTime().Ticks;
                }
                catch
                {
                    // Protected processes can deny start-time access.
                }
                try
                {
                    exePath = process.MainModule?.FileName ?? string.Empty;
                }
                catch
                {
                    // Access denied for system processes — expected
                }

                long cpuTime = 0;
                try { cpuTime = process.TotalProcessorTime.Ticks; }
                catch (System.ComponentModel.Win32Exception) { }
                catch (InvalidOperationException) { }

                results.Add(new ProcessResourceData
                {
                    ProcessId = process.Id,
                    StartMarker = startMarker,
                    ProcessName = process.ProcessName,
                    ExecutablePath = exePath,
                    PrivateBytes = process.PrivateMemorySize64,
                    WorkingSetBytes = process.WorkingSet64,
                    PrivateWorkingSetBytes = includePrivateWorkingSet ? ReadPrivateWorkingSet(process) : null,
                    CpuTimeTicks = cpuTime
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
