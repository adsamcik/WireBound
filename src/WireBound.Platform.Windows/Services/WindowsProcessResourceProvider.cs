using System.Diagnostics;
using System.Runtime.Versioning;
using System.Runtime.InteropServices;
using System.Text;
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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(SafeProcessHandle handle, uint flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(SafeProcessHandle handle, out long created, out long exited, out long kernel, out long user);

    internal static bool IsCurrentInstance(ProcessResourceData sample)
    {
        using var handle = OpenProcess(0x1000, false, sample.ProcessId);
        return MatchesStart(handle, sample.StartMarker);
    }

    private static bool MatchesStart(SafeProcessHandle handle, long marker)
    {
        if (marker == 0 || handle.IsInvalid || !GetProcessTimes(handle, out var created, out _, out _, out _)) return false;
        try { return DateTime.FromFileTimeUtc(created).Ticks == marker; }
        catch (ArgumentOutOfRangeException) { return false; }
    }

    internal static string ReadExecutablePath(int processId, long startMarker)
    {
        using var handle = OpenProcess(0x1000, false, processId);
        if (!MatchesStart(handle, startMarker)) return "";
        // Limited-information queries work for service hosts without reading their modules.
        var name = new StringBuilder(1024);
        var size = name.Capacity;
        if (QueryFullProcessImageNameW(handle, 0, name, ref size)) return name.ToString();
        if (Marshal.GetLastWin32Error() != 122) return ""; // ERROR_INSUFFICIENT_BUFFER
        name.EnsureCapacity(32768);
        size = name.Capacity;
        return QueryFullProcessImageNameW(handle, 0, name, ref size) ? name.ToString() : "";
    }

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
        var samples = CaptureResources(cancellationToken, includePrivateWorkingSet);
        return Task.FromResult(WindowsServiceHostSnapshot.Enrich(samples, cancellationToken));
    }

    private static IReadOnlyList<ProcessResourceData> CaptureResources(CancellationToken cancellationToken, bool includePrivateWorkingSet)
    {
        var results = new List<ProcessResourceData>();

        var snapshot = WindowsProcessMemorySnapshot.Capture(includePrivateWorkingSet, cancellationToken);
        if (snapshot is not null)
        {
            foreach (var sample in snapshot)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = ReadExecutablePath(sample.ProcessId, sample.StartMarker);
                results.Add(new ProcessResourceData
                {
                    ProcessId = sample.ProcessId,
                    ProcessName = sample.ProcessName,
                    StartMarker = sample.StartMarker,
                    ExecutablePath = path,
                    WorkingSetBytes = sample.WorkingSetBytes,
                    PrivateBytes = sample.PrivateBytes,
                    PrivateWorkingSetBytes = sample.PrivateWorkingSetBytes,
                    CpuTimeTicks = sample.CpuTimeTicks
                });
            }
            return results;
        }

        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch
        {
            return results;
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
                    exePath = ReadExecutablePath(process.Id, startMarker);
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

        return results;
    }
}
