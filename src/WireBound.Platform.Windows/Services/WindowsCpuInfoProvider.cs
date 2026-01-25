using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Windows.Services;

/// <summary>
/// Windows implementation of CPU info provider using Performance Counters
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsCpuInfoProvider : ICpuInfoProvider, IDisposable
{
    private readonly PerformanceCounter _cpuCounter;
    private readonly PerformanceCounter[] _perCoreCounters;
    private readonly int _processorCount;
    private readonly string _processorName;
    private bool _disposed;

    public WindowsCpuInfoProvider()
    {
        _processorCount = Environment.ProcessorCount;
        _processorName = GetProcessorNameFromRegistry();

        // Initialize performance counters
        _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", true);

        // Initialize per-core counters
        _perCoreCounters = new PerformanceCounter[_processorCount];
        for (int i = 0; i < _processorCount; i++)
        {
            _perCoreCounters[i] = new PerformanceCounter("Processor", "% Processor Time", i.ToString(), true);
        }

        // First call to NextValue() returns 0, need to prime the counters
        _ = _cpuCounter.NextValue();
        foreach (var counter in _perCoreCounters)
        {
            _ = counter.NextValue();
        }
    }

    private static string GetProcessorNameFromRegistry()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return key?.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? "Unknown Processor";
        }
        catch
        {
            return "Unknown Processor";
        }
    }

    public string GetProcessorName() => _processorName;

    public int GetProcessorCount() => _processorCount;

    public CpuInfoData GetCpuInfo()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var totalUsage = _cpuCounter.NextValue();
        var perCoreUsage = new double[_processorCount];

        for (int i = 0; i < _processorCount; i++)
        {
            perCoreUsage[i] = _perCoreCounters[i].NextValue();
        }

        return new CpuInfoData
        {
            UsagePercent = totalUsage,
            PerCoreUsagePercent = perCoreUsage,
            ProcessorCount = _processorCount,
            FrequencyMhz = GetCurrentFrequency(),
            TemperatureCelsius = null // Would require WMI or LibreHardwareMonitor for temps
        };
    }

    private double? GetCurrentFrequency()
    {
        try
        {
            // Get base frequency from registry
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            var mhz = key?.GetValue("~MHz");
            if (mhz is int mhzValue)
            {
                return mhzValue;
            }
        }
        catch
        {
            // Ignore errors
        }

        return null;
    }

    public bool SupportsPerCoreUsage => true;
    public bool SupportsTemperature => false; // Would need LibreHardwareMonitor
    public bool SupportsFrequency => true;

    public void Dispose()
    {
        if (_disposed) return;

        _cpuCounter.Dispose();
        foreach (var counter in _perCoreCounters)
        {
            counter.Dispose();
        }

        _disposed = true;
    }
}
