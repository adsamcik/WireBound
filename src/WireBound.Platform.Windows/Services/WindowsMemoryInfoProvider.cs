using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Windows.Services;

/// <summary>
/// Windows implementation of memory info provider using GlobalMemoryStatusEx
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsMemoryInfoProvider : IMemoryInfoProvider
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicallyInstalledSystemMemory(out ulong totalMemoryInKilobytes);

    [StructLayout(LayoutKind.Sequential)]
    private struct PERFORMANCE_INFORMATION
    {
        public uint cb;
        public nuint CommitTotal;
        public nuint CommitLimit;
        public nuint CommitPeak;
        public nuint PhysicalTotal;
        public nuint PhysicalAvailable;
        public nuint SystemCache;
        public nuint KernelTotal;
        public nuint KernelPaged;
        public nuint KernelNonpaged;
        public nuint PageSize;
        public uint HandleCount;
        public uint ProcessCount;
        public uint ThreadCount;
    }

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPerformanceInfo(ref PERFORMANCE_INFORMATION info, uint size);

    public MemoryInfoData GetMemoryInfo()
    {
        var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };

        if (!GlobalMemoryStatusEx(ref memStatus))
        {
            // GC data describes this process, not the machine.
            return new MemoryInfoData();
        }

        long? installedBytes = null;
        if (GetPhysicallyInstalledSystemMemory(out var installedKilobytes)
            && installedKilobytes <= long.MaxValue / 1024UL)
        {
            installedBytes = (long)(installedKilobytes * 1024UL);
        }

        long? nonpagedPoolBytes = null;
        var performance = new PERFORMANCE_INFORMATION { cb = (uint)Marshal.SizeOf<PERFORMANCE_INFORMATION>() };
        if (GetPerformanceInfo(ref performance, performance.cb)
            && performance.PageSize > 0
            && (ulong)performance.KernelNonpaged <= (ulong)long.MaxValue / (ulong)performance.PageSize)
        {
            nonpagedPoolBytes = (long)((ulong)performance.KernelNonpaged * (ulong)performance.PageSize);
        }

        return new MemoryInfoData
        {
            InstalledBytes = installedBytes,
            NonpagedPoolBytes = nonpagedPoolBytes,
            TotalBytes = (long)memStatus.ullTotalPhys,
            AvailableBytes = (long)memStatus.ullAvailPhys,
            UsedBytes = (long)(memStatus.ullTotalPhys - memStatus.ullAvailPhys),
            TotalVirtualBytes = (long)memStatus.ullTotalPageFile,
            UsedVirtualBytes = (long)(memStatus.ullTotalPageFile - memStatus.ullAvailPageFile)
        };
    }

    public long GetTotalPhysicalMemory()
    {
        var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };

        if (GlobalMemoryStatusEx(ref memStatus))
        {
            return (long)memStatus.ullTotalPhys;
        }

        return 0;
    }

    public bool SupportsVirtualMemory => true;
}
