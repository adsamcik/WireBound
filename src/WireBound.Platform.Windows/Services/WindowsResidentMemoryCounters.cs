using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using WireBound.Platform.Abstract.Models;

namespace WireBound.Platform.Windows.Services;

/// <summary>One-shot, language-neutral resident gauges. No WMI or persistent polling.</summary>
[SupportedOSPlatform("windows")]
internal static class WindowsResidentMemoryCounters
{
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct CounterValue
    {
        [FieldOffset(0)] public uint Status;
        [FieldOffset(8)] public long Value;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQueryW(string? source, nuint userData, out nint query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounterW(nint query, string path, nuint userData, out nint counter);
    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(nint query);
    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterValue(nint counter, uint format, out uint type, out CounterValue value);
    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(nint query);

    internal static IReadOnlyList<ResidentMemoryAllocation> Read()
    {
        // GetPerformanceInfo.SystemCache also covers standby memory.
        // Use only the resident gauge; standby remains part of Available.
        var counters = new (string Counter, string Id, string Name, string Metric, string Detail)[]
        {
            ("Pool Paged Resident Bytes", "paged-pool", "Resident paged kernel pool", "Kernel RAM", "Paged kernel allocations currently resident in RAM; excludes paged-out pool."),
            ("System Cache Resident Bytes", "file-cache", "Resident system cache", "Resident cache", "Resident system-cache working set. Excludes standby cache already counted as Available."),
            ("System Code Resident Bytes", "kernel-code", "Resident kernel code", "Kernel RAM", "Operating-system code currently resident in RAM."),
            ("System Driver Resident Bytes", "drivers", "Resident driver code", "Kernel RAM", "Driver code currently resident in RAM; excludes driver-locked allocations not exposed by this counter."),
            ("Modified Page List Bytes", "modified", "Modified pages awaiting writeback", "Modified pages", "Physical pages removed from working sets, awaiting writeback before reuse. These pages are not Available.")
        };
        if (PdhOpenQueryW(null, 0, out var query) != 0) return [];
        try
        {
            var handles = new nint[counters.Length];
            for (var i = 0; i < counters.Length; i++)
                if (PdhAddEnglishCounterW(query, @"\Memory\" + counters[i].Counter, 0, out handles[i]) != 0) handles[i] = 0;
            if (PdhCollectQueryData(query) != 0) return [];
            var result = new List<ResidentMemoryAllocation>();
            for (var i = 0; i < counters.Length; i++)
                if (handles[i] != 0 && PdhGetFormattedCounterValue(handles[i], 0x400, out _, out var value) == 0
                    && value.Status is 0 or 1 && value.Value >= 0)
                {
                    var counter = counters[i];
                    result.Add(new(counter.Id, counter.Name, value.Value, counter.Metric,
                        $"Windows Memory\\{counter.Counter} · {counter.Detail}"));
                }
            return result;
        }
        finally { PdhCloseQuery(query); }
    }
}
