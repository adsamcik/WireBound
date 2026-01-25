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

    public MemoryInfoData GetMemoryInfo()
    {
        var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };

        if (!GlobalMemoryStatusEx(ref memStatus))
        {
            // Fallback to GC memory info if native call fails
            var gcInfo = GC.GetGCMemoryInfo();
            return new MemoryInfoData
            {
                TotalBytes = gcInfo.TotalAvailableMemoryBytes,
                AvailableBytes = gcInfo.TotalAvailableMemoryBytes - gcInfo.MemoryLoadBytes,
                UsedBytes = gcInfo.MemoryLoadBytes,
                TotalVirtualBytes = 0,
                UsedVirtualBytes = 0
            };
        }

        return new MemoryInfoData
        {
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

        return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
    }

    public bool SupportsVirtualMemory => true;
}
