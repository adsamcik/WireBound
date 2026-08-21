using System.Globalization;
using System.Runtime.Versioning;
using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Linux.Services;

/// <summary>
/// Linux implementation of CPU info provider using /proc filesystem
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxCpuInfoProvider : ICpuInfoProvider
{
    private readonly int _processorCount;
    private readonly string _processorName;
    private long[] _previousIdle;
    private long[] _previousTotal;
    private long[] _previousKernel;
    private long _previousTotalIdle;
    private long _previousTotalSum;
    private long _previousTotalKernel;

    public LinuxCpuInfoProvider()
    {
        _processorCount = Environment.ProcessorCount;
        _processorName = GetProcessorNameFromCpuInfo();
        _previousIdle = new long[_processorCount];
        _previousTotal = new long[_processorCount];
        _previousKernel = new long[_processorCount];
    }

    private static string GetProcessorNameFromCpuInfo()
    {
        try
        {
            if (File.Exists("/proc/cpuinfo"))
            {
                var lines = File.ReadAllLines("/proc/cpuinfo");
                foreach (var line in lines)
                {
                    if (line.StartsWith("model name", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split(':', 2);
                        if (parts.Length > 1)
                        {
                            return parts[1].Trim();
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore errors
        }

        return "Unknown Processor";
    }

    public string GetProcessorName() => _processorName;

    public int GetProcessorCount() => _processorCount;

    public CpuInfoData GetCpuInfo()
    {
        double usagePercent = 0;
        double kernelUsagePercent = 0;
        var perCoreUsage = new double[_processorCount];
        var perCoreKernelUsage = new double[_processorCount];
        double? frequencyMhz = null;
        double? temperatureCelsius = null;

        try
        {
            if (File.Exists("/proc/stat"))
            {
                var lines = File.ReadAllLines("/proc/stat");

                foreach (var line in lines)
                {
                    if (line.StartsWith("cpu ", StringComparison.Ordinal))
                    {
                        // Total CPU usage
                        var (idle, total, kernel) = ParseCpuTimings(line);

                        if (_previousTotalSum > 0)
                        {
                            var idleDelta = idle - _previousTotalIdle;
                            var totalDelta = total - _previousTotalSum;
                            var kernelDelta = kernel - _previousTotalKernel;

                            if (totalDelta > 0)
                            {
                                usagePercent = ClampPercent((1.0 - (double)idleDelta / totalDelta) * 100.0);
                                kernelUsagePercent = Math.Min(
                                    usagePercent,
                                    ClampPercent((double)kernelDelta / totalDelta * 100.0));
                            }
                        }

                        _previousTotalIdle = idle;
                        _previousTotalSum = total;
                        _previousTotalKernel = kernel;
                    }
                    else if (line.StartsWith("cpu", StringComparison.Ordinal))
                    {
                        // Per-core usage (cpu0, cpu1, etc.)
                        var coreIdStr = line.AsSpan(3, line.IndexOf(' ') - 3);
                        if (int.TryParse(coreIdStr, out var coreId) && coreId < _processorCount)
                        {
                            var (idle, total, kernel) = ParseCpuTimings(line);

                            if (_previousTotal[coreId] > 0)
                            {
                                var idleDelta = idle - _previousIdle[coreId];
                                var totalDelta = total - _previousTotal[coreId];
                                var kernelDelta = kernel - _previousKernel[coreId];

                                if (totalDelta > 0)
                                {
                                    perCoreUsage[coreId] = ClampPercent((1.0 - (double)idleDelta / totalDelta) * 100.0);
                                    perCoreKernelUsage[coreId] = Math.Min(
                                        perCoreUsage[coreId],
                                        ClampPercent((double)kernelDelta / totalDelta * 100.0));
                                }
                            }

                            _previousIdle[coreId] = idle;
                            _previousTotal[coreId] = total;
                            _previousKernel[coreId] = kernel;
                        }
                    }
                }
            }
        }
        catch
        {
            // Return default stats on error
        }

        frequencyMhz = GetCurrentFrequency();
        temperatureCelsius = GetTemperature();

        return new CpuInfoData
        {
            UsagePercent = usagePercent,
            PerCoreUsagePercent = perCoreUsage,
            KernelUsagePercent = kernelUsagePercent,
            PerCoreKernelUsagePercent = perCoreKernelUsage,
            ProcessorCount = _processorCount,
            FrequencyMhz = frequencyMhz,
            TemperatureCelsius = temperatureCelsius
        };
    }

    internal static (double usage, long idle, long total) ParseCpuLine(string line)
    {
        var (idle, total, _) = ParseCpuTimings(line);
        return (0, idle, total);
    }

    /// <summary>
    /// Parses cumulative Linux CPU ticks. Kernel ticks include system, IRQ,
    /// and soft-IRQ work and are a subset of non-idle CPU time.
    /// </summary>
    internal static (long idle, long total, long kernel) ParseCpuTimings(string line)
    {
        // Format: cpu user nice system idle iowait irq softirq steal guest guest_nice
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 5)
            return (0, 0, 0);

        long user = long.Parse(parts[1], CultureInfo.InvariantCulture);
        long nice = long.Parse(parts[2], CultureInfo.InvariantCulture);
        long system = long.Parse(parts[3], CultureInfo.InvariantCulture);
        long idle = long.Parse(parts[4], CultureInfo.InvariantCulture);
        long iowait = parts.Length > 5 ? long.Parse(parts[5], CultureInfo.InvariantCulture) : 0;
        long irq = parts.Length > 6 ? long.Parse(parts[6], CultureInfo.InvariantCulture) : 0;
        long softirq = parts.Length > 7 ? long.Parse(parts[7], CultureInfo.InvariantCulture) : 0;
        long steal = parts.Length > 8 ? long.Parse(parts[8], CultureInfo.InvariantCulture) : 0;

        long total = user + nice + system + idle + iowait + irq + softirq + steal;
        long idleTotal = idle + iowait;
        long kernel = system + irq + softirq;

        return (idleTotal, total, kernel);
    }

    private static double ClampPercent(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0;

    private static double? GetCurrentFrequency()
    {
        try
        {
            // Try to read current frequency from scaling_cur_freq
            const string path = "/sys/devices/system/cpu/cpu0/cpufreq/scaling_cur_freq";
            if (File.Exists(path))
            {
                var freqKhz = long.Parse(File.ReadAllText(path).Trim(), CultureInfo.InvariantCulture);
                return freqKhz / 1000.0; // Convert to MHz
            }
        }
        catch
        {
            // Ignore errors
        }

        return null;
    }

    private static double? GetTemperature()
    {
        try
        {
            // Try common thermal zone paths
            string[] thermalPaths =
            [
                "/sys/class/thermal/thermal_zone0/temp",
                "/sys/class/hwmon/hwmon0/temp1_input",
                "/sys/class/hwmon/hwmon1/temp1_input"
            ];

            foreach (var path in thermalPaths)
            {
                if (File.Exists(path))
                {
                    var tempMilliC = long.Parse(File.ReadAllText(path).Trim(), CultureInfo.InvariantCulture);
                    return tempMilliC / 1000.0; // Convert to Celsius
                }
            }
        }
        catch
        {
            // Ignore errors
        }

        return null;
    }

    public bool SupportsPerCoreUsage => true;
    public bool SupportsTemperature => File.Exists("/sys/class/thermal/thermal_zone0/temp");
    public bool SupportsFrequency => File.Exists("/sys/devices/system/cpu/cpu0/cpufreq/scaling_cur_freq");
}
