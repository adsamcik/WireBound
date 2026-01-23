using System.Globalization;
using System.Runtime.Versioning;
using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Linux.Services;

/// <summary>
/// Linux implementation of memory info provider using /proc/meminfo
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxMemoryInfoProvider : IMemoryInfoProvider
{
    public MemoryInfoData GetMemoryInfo()
    {
        try
        {
            if (File.Exists("/proc/meminfo"))
            {
                var memInfo = ParseMemInfo();
                
                // MemTotal is total physical memory
                var totalBytes = memInfo.GetValueOrDefault("MemTotal", 0) * 1024;
                
                // MemAvailable is the best estimate of available memory
                // Falls back to MemFree + Buffers + Cached if MemAvailable not present
                long availableBytes;
                if (memInfo.TryGetValue("MemAvailable", out var available))
                {
                    availableBytes = available * 1024;
                }
                else
                {
                    var memFree = memInfo.GetValueOrDefault("MemFree", 0);
                    var buffers = memInfo.GetValueOrDefault("Buffers", 0);
                    var cached = memInfo.GetValueOrDefault("Cached", 0);
                    availableBytes = (memFree + buffers + cached) * 1024;
                }
                
                var usedBytes = totalBytes - availableBytes;
                
                // Swap for virtual memory
                var swapTotal = memInfo.GetValueOrDefault("SwapTotal", 0) * 1024;
                var swapFree = memInfo.GetValueOrDefault("SwapFree", 0) * 1024;
                
                return new MemoryInfoData
                {
                    TotalBytes = totalBytes,
                    AvailableBytes = availableBytes,
                    UsedBytes = usedBytes,
                    TotalVirtualBytes = totalBytes + swapTotal,
                    UsedVirtualBytes = usedBytes + (swapTotal - swapFree)
                };
            }
        }
        catch
        {
            // Fallback below
        }
        
        // Fallback to GC memory info
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

    private static Dictionary<string, long> ParseMemInfo()
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var line in File.ReadLines("/proc/meminfo"))
        {
            // Format: "MemTotal:       16384000 kB"
            var colonIndex = line.IndexOf(':');
            if (colonIndex <= 0) continue;
            
            var key = line[..colonIndex].Trim();
            var valuePart = line[(colonIndex + 1)..].Trim();
            
            // Remove "kB" suffix and parse
            var spaceIndex = valuePart.IndexOf(' ');
            var valueStr = spaceIndex > 0 ? valuePart[..spaceIndex] : valuePart;
            
            if (long.TryParse(valueStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                result[key] = value;
            }
        }
        
        return result;
    }

    public long GetTotalPhysicalMemory()
    {
        try
        {
            var memInfo = ParseMemInfo();
            return memInfo.GetValueOrDefault("MemTotal", 0) * 1024;
        }
        catch
        {
            return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        }
    }

    public bool SupportsVirtualMemory => true;
}
