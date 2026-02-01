using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Stub.Services;

/// <summary>
/// Stub implementation of CPU info provider for unsupported platforms
/// </summary>
public sealed class StubCpuInfoProvider : ICpuInfoProvider
{
    private readonly Random _random = new();
    private readonly int _processorCount;

    public StubCpuInfoProvider()
    {
        _processorCount = Environment.ProcessorCount;
    }

    public string GetProcessorName() => "Stub Processor";

    public int GetProcessorCount() => _processorCount;

    public CpuInfoData GetCpuInfo()
    {
        // Generate realistic-looking random CPU usage for development/testing
        var baseUsage = _random.NextDouble() * 30 + 10; // 10-40% base usage
        var perCore = new double[_processorCount];

        for (int i = 0; i < _processorCount; i++)
        {
            perCore[i] = Math.Clamp(baseUsage + (_random.NextDouble() * 20 - 10), 0, 100);
        }

        return new CpuInfoData
        {
            UsagePercent = baseUsage,
            PerCoreUsagePercent = perCore,
            ProcessorCount = _processorCount,
            FrequencyMhz = 3500 + _random.NextDouble() * 500, // 3500-4000 MHz
            TemperatureCelsius = null // Not supported in stub
        };
    }

    public bool SupportsPerCoreUsage => true;
    public bool SupportsTemperature => false;
    public bool SupportsFrequency => true;
}
