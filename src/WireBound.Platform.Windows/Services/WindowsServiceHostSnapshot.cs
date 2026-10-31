using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using WireBound.Platform.Abstract.Models;

namespace WireBound.Platform.Windows.Services;

/// <summary>One read-only SCM enumeration per resource sample; no service control or guest commands.</summary>
[SupportedOSPlatform("windows")]
internal static class WindowsServiceHostSnapshot
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint Type, State, AcceptedControls, ExitCode, ServiceExitCode, Checkpoint, WaitHint, ProcessId, Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceEntry
    {
        public nint Name, DisplayName;
        public ServiceStatus Status;
    }

    private sealed class ServiceManagerHandle() : SafeHandleZeroOrMinusOneIsInvalid(true)
    {
        protected override bool ReleaseHandle() => CloseServiceHandle(handle);
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ServiceManagerHandle OpenSCManagerW(string? machine, string? database, uint access);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(nint handle);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumServicesStatusExW(ServiceManagerHandle manager, int level, uint type, uint state,
        nint buffer, int capacity, out int needed, out int returned, ref uint resume, string? group);

    internal static IReadOnlyDictionary<int, IReadOnlyList<HostedServiceInfo>> Capture(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var manager = OpenSCManagerW(null, null, 0x4); // SC_MANAGER_ENUMERATE_SERVICE
        if (manager.IsInvalid) return new Dictionary<int, IReadOnlyList<HostedServiceInfo>>();
        const int capacity = 256 * 1024; // Documented maximum SCM page size.
        var buffer = Marshal.AllocHGlobal(capacity);
        var services = new Dictionary<int, List<HostedServiceInfo>>();
        var entrySize = Marshal.SizeOf<ServiceEntry>();
        uint resume = 0;
        try
        {
            for (var page = 0; page < 32; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var previousResume = resume;
                var success = EnumServicesStatusExW(manager, 0, 0x30, 1, buffer, capacity, out _, out var returned, ref resume, null);
                var error = success ? 0 : Marshal.GetLastWin32Error();
                if (!success && error != 234 || returned < 0 || returned > capacity / entrySize) break;
                for (var i = 0; i < returned; i++)
                {
                    var entry = Marshal.PtrToStructure<ServiceEntry>(buffer + i * entrySize);
                    // Pending start/stop states do not provide reliable process associations.
                    if (entry.Status.ProcessId is 0 or > int.MaxValue || entry.Status.State is < 4 or > 7) continue;
                    var name = Marshal.PtrToStringUni(entry.Name);
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var display = Marshal.PtrToStringUni(entry.DisplayName) ?? name;
                    var pid = (int)entry.Status.ProcessId;
                    if (!services.TryGetValue(pid, out var list)) services[pid] = list = [];
                    list.Add(new(name, display));
                }
                if (success)
                    return services.ToDictionary(p => p.Key, p => (IReadOnlyList<HostedServiceInfo>)p.Value
                        .DistinctBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                        .OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToArray());
                if (resume == previousResume || returned == 0) break;
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        // Do not present a failed or incomplete enumeration as a complete service list.
        return new Dictionary<int, IReadOnlyList<HostedServiceInfo>>();
    }

    internal static IReadOnlyList<ProcessResourceData> Enrich(IReadOnlyList<ProcessResourceData> samples, CancellationToken cancellationToken)
    {
        if (!samples.Any(IsCandidate)) return samples;
        // The resource generations are sampled BEFORE SCM, then checked again afterwards.
        // A reused PID or inaccessible generation must never inherit another host's services.
        var services = Capture(cancellationToken);
        // Standard users cannot open most system service processes. The native
        // process list still exposes their generations, without elevation.
        var generations = WindowsProcessMemorySnapshot.Capture(false, cancellationToken)?.ToDictionary(p => p.ProcessId, p => p.StartMarker);
        return Enrich(samples, services, sample => generations is not null
            ? generations.TryGetValue(sample.ProcessId, out var marker) && marker == sample.StartMarker
            : WindowsProcessResourceProvider.IsCurrentInstance(sample), cancellationToken);
    }

    internal static IReadOnlyList<ProcessResourceData> Enrich(IReadOnlyList<ProcessResourceData> samples,
        IReadOnlyDictionary<int, IReadOnlyList<HostedServiceInfo>> services, Func<ProcessResourceData, bool> isCurrentInstance,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ProcessResourceData>(samples.Count);
        foreach (var sample in samples)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCandidate(sample) || !string.IsNullOrEmpty(sample.ExecutablePath) && !IsSystemHostPath(sample.ExecutablePath))
            {
                results.Add(sample);
                continue;
            }
            var names = sample.StartMarker != 0 && isCurrentInstance(sample) && services.TryGetValue(sample.ProcessId, out var observed)
                ? observed : [];
            if (!IsSystemHostPath(sample.ExecutablePath) && names.Count == 0)
            {
                results.Add(sample);
                continue;
            }
            results.Add(new ProcessResourceData
            {
                ProcessId = sample.ProcessId,
                StartMarker = sample.StartMarker,
                ProcessName = sample.ProcessName,
                ExecutablePath = sample.ExecutablePath,
                PrivateBytes = sample.PrivateBytes,
                WorkingSetBytes = sample.WorkingSetBytes,
                PrivateWorkingSetBytes = sample.PrivateWorkingSetBytes,
                CpuTimeTicks = sample.CpuTimeTicks,
                ServiceHost = new ServiceHostInfo(names)
            });
        }
        return results;
    }

    private static bool IsCandidate(ProcessResourceData sample) => sample.ProcessName.Equals("svchost", StringComparison.OrdinalIgnoreCase)
        || sample.ProcessName.Equals("svchost.exe", StringComparison.OrdinalIgnoreCase);

    private static bool IsSystemHostPath(string path)
    {
        var systemX86 = Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);
        return !string.IsNullOrEmpty(path)
            && (string.Equals(path, Path.Combine(Environment.SystemDirectory, "svchost.exe"), StringComparison.OrdinalIgnoreCase)
                || systemX86.Length > 0 && string.Equals(path, Path.Combine(systemX86, "svchost.exe"), StringComparison.OrdinalIgnoreCase));
    }
}
