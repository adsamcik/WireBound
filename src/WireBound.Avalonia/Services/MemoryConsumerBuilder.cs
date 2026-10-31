using WireBound.Avalonia.ViewModels;
using WireBound.Core.Models;
using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Avalonia.Services;

/// <summary>Builds exclusive physical attribution and explicitly separate workload observations.</summary>
public static class MemoryConsumerBuilder
{
    public static IReadOnlyList<MemoryConsumerRow> Build(MemoryInfoData memory,
        IReadOnlyList<ProcessUsageSnapshot> processes, IReadOnlyList<MemoryWorkload> workloads,
        IWorkloadHostProvider hosts, bool validPhysicalSample)
    {
        var appProcesses = new List<ProcessUsageSnapshot>();
        var hostProcesses = new List<(ProcessUsageSnapshot Process, WorkloadHost Host)>();
        foreach (var process in processes)
        {
            var host = hosts.IdentifyHost(process.ProcessName, process.ExecutablePath);
            if (host is null) appProcesses.Add(process);
            else hostProcesses.Add((process, host));
        }
        var pool = validPhysicalSample && memory.NonpagedPoolBytes is >= 0
            && memory.NonpagedPoolBytes <= memory.UsedBytes ? memory.NonpagedPoolBytes : null;
        var measured = processes.Where(p => p.PrivateWorkingSetBytes is >= 0
            && p.PrivateWorkingSetBytes <= p.WorkingSetBytes).Sum(p => p.PrivateWorkingSetBytes!.Value);
        // Collection instants can disagree. Never force an overfull sample to reconcile.
        var reconciles = validPhysicalSample && measured <= memory.UsedBytes - (pool ?? 0);
        var systemBudget = reconciles ? memory.UsedBytes - measured - (pool ?? 0) : 0;
        var detailedSystemReconciles = reconciles;
        foreach (var allocation in memory.SystemAllocations)
        {
            if (allocation.Bytes < 0 || allocation.Bytes > systemBudget)
            {
                detailedSystemReconciles = false;
                break;
            }
            systemBudget -= allocation.Bytes;
        }
        var systemBytes = (pool ?? 0) + (detailedSystemReconciles ? memory.UsedBytes - measured - (pool ?? 0) - systemBudget : 0);
        long? Physical(ProcessUsageSnapshot p) => reconciles && p.PrivateWorkingSetBytes is >= 0
            && p.PrivateWorkingSetBytes <= p.WorkingSetBytes ? p.PrivateWorkingSetBytes : null;

        var apps = new MemoryConsumerRow
        {
            Id = "apps",
            Name = "Applications and background processes",
            IsExpanded = true,
            PhysicalBytes = appProcesses.Any(p => Physical(p).HasValue) ? appProcesses.Sum(p => Physical(p) ?? 0) : null,
            IsPartial = appProcesses.Any(p => !Physical(p).HasValue),
            Summary = $"{appProcesses.Count} processes",
            Detail = "Private RAM counts measured resident pages exclusive to each process. Shared mappings and unclassified allocations remain in Shared and other. App groups use the exact executable path, or the process name when the path is unavailable."
        };
        foreach (var group in appProcesses.GroupBy(p => string.IsNullOrEmpty(p.ExecutablePath)
                     ? "name:" + p.ProcessName : "path:" + p.ExecutablePath,
                     OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
        {
            var members = group.OrderByDescending(p => p.WorkingSetBytes).ToArray();
            var physicalValues = members.Select(Physical).ToArray();
            var bytes = physicalValues.Any(p => p.HasValue) ? physicalValues.Sum(p => p ?? 0) : (long?)null;
            var instances = members.Where(p => p.StartMarker != 0)
                .Select(p => new ProcessInstanceId(p.ProcessId, p.StartMarker)).ToHashSet();
            var row = new MemoryConsumerRow
            {
                Id = "app:" + group.Key,
                Name = members[0].ProcessName,
                ExecutablePath = members[0].ExecutablePath,
                Summary = members.Length == 1 ? $"PID {members[0].ProcessId}" : $"{members.Length} processes",
                PhysicalBytes = bytes,
                ObservedBytes = members.Sum(p => Math.Max(0, p.WorkingSetBytes)),
                IsPartial = physicalValues.Any(p => !p.HasValue),
                Metric = "Working set",
                Processes = instances,
                HasConfirmedProcessLinks = true,
                Detail = $"{members[0].ExecutablePath}\n{physicalValues.Count(v => v.HasValue)} of {members.Length} processes have measured private RAM. Working sets include shared pages and can overlap other rows."
            };
            foreach (var member in members.Length > 1 ? members : [])
            {
                var privateBytes = Physical(member);
                row.Children.Add(new MemoryConsumerRow
                {
                    Id = $"process:{member.ProcessId}:{member.StartMarker}",
                    Name = member.ProcessName,
                    ExecutablePath = member.ExecutablePath,
                    Summary = $"PID {member.ProcessId}",
                    PhysicalBytes = privateBytes,
                    HasConfirmedProcessLinks = true,
                    ObservedBytes = Math.Max(0, member.WorkingSetBytes),
                    Metric = "Working set",
                    Detail = $"{member.ExecutablePath}\nPrivate RAM is resident, not committed memory. Working set includes shared pages."
                        + (privateBytes is null ? " Private RAM is unavailable for this process or sample." : ""),
                    Processes = member.StartMarker == 0 ? new HashSet<ProcessInstanceId>() : [new(member.ProcessId, member.StartMarker)]
                });
            }
            apps.Children.Add(row);
        }
        apps.Children.Sort(Compare);
        var virtualMachines = new MemoryConsumerRow
        {
            Id = "workloads",
            Name = "Virtual machines and containers",
            IsExpanded = true,
            PhysicalBytes = hostProcesses.Any(p => Physical(p.Process).HasValue) ? hostProcesses.Sum(p => Physical(p.Process) ?? 0) : null,
            IsPartial = hostProcesses.Any(p => !Physical(p.Process).HasValue),
            Detail = "Private RAM includes only measured host-process private pages. Assigned VM memory, Docker usage, and process RSS are observations in the other column; they can overlap each other and host RAM. Guests are never started or entered to collect data."
        };
        foreach (var (process, host) in hostProcesses)
        {
            var privateBytes = Physical(process);
            virtualMachines.Children.Add(new MemoryConsumerRow
            {
                Id = $"host:{process.ProcessId}:{process.StartMarker}",
                Name = host.Label,
                ExecutablePath = process.ExecutablePath,
                IconKey = "WbAdapterVirtual",
                Summary = $"PID {process.ProcessId}",
                PhysicalBytes = privateBytes,
                ObservedBytes = Math.Max(0, process.WorkingSetBytes),
                Metric = "Host working set",
                Detail = host.Evidence + ". Host working set is not the complete guest footprint. Guest process memory requires a guest collector.",
                Processes = process.StartMarker == 0 ? new HashSet<ProcessInstanceId>() : [new(process.ProcessId, process.StartMarker)]
            });
        }
        virtualMachines.Children.AddRange(workloads.Select(FromWorkload));
        virtualMachines.Children.Sort(Compare);
        var system = new MemoryConsumerRow
        {
            Id = "system",
            Name = "System and drivers",
            PhysicalBytes = pool.HasValue || detailedSystemReconciles && memory.SystemAllocations.Count > 0 ? systemBytes : null,
            Detail = "Independently measured resident kernel, cache, and modified-page allocations. Standby cache is part of Available."
        };
        if (pool.HasValue)
            system.Children.Add(new MemoryConsumerRow
            {
                Id = "nonpaged",
                Name = "Nonpaged kernel pool",
                IconKey = "WbNavSystem",
                PhysicalBytes = pool,
                Detail = "Windows GetPerformanceInfo · resident, nonpageable kernel pool."
            });
        foreach (var allocation in memory.SystemAllocations)
            system.Children.Add(new MemoryConsumerRow
            {
                Id = "system:" + allocation.Id,
                Name = allocation.Name,
                IconKey = "WbNavSystem",
                PhysicalBytes = detailedSystemReconciles ? allocation.Bytes : null,
                ObservedBytes = !detailedSystemReconciles && allocation.Bytes >= 0 ? allocation.Bytes : null,
                Metric = detailedSystemReconciles ? allocation.Metric : "Unreconciled observation",
                Detail = allocation.Detail + (detailedSystemReconciles ? "" : " This separately sampled category does not reconcile with current in-use RAM; it is not subtracted from the remainder. Refresh to retry.")
            });
        var privateReadings = processes.Count(p => Physical(p).HasValue);
        var remainder = new MemoryConsumerRow
        {
            Id = "other",
            Name = "Shared and other",
            IconKey = "WbEntityMemory",
            PhysicalBytes = reconciles ? memory.UsedBytes - measured - systemBytes : null,
            Summary = "Unattributed",
            Detail = reconciles
                ? $"Private resident RAM measured for {privateReadings} of {processes.Count} processes. In-use RAM minus those private pages and measured resident system allocations. The remainder includes shared images/mappings and unclassified kernel, driver-locked or guest allocations. Standby cache is already Available, not part of this remainder."
                    + (memory.SystemAllocations.Count == 0 ? " Detailed system residency counters were not available in this sample." : "")
                    + (!detailedSystemReconciles ? " Additional system observations did not reconcile and were not subtracted." : "")
                : "Physical attribution is unavailable because the capacity sample is missing or separately sampled allocations exceed in-use RAM. Refresh to retry; observations remain available."
        };
        if (reconciles) AddSharedDiagnostics(remainder, apps.Children.Concat(virtualMachines.Children
            .Where(r => r.Id.StartsWith("host:", StringComparison.Ordinal))).Where(r => r.ObservedBytes.HasValue));
        return [apps, virtualMachines, system, remainder];
    }

    private static void AddSharedDiagnostics(MemoryConsumerRow remainder, IEnumerable<MemoryConsumerRow> consumers)
    {
        foreach (var consumer in consumers)
        {
            var incomplete = consumer.IsPartial || !consumer.PhysicalBytes.HasValue;
            var bytes = incomplete ? consumer.ObservedBytes : consumer.ObservedBytes - consumer.PhysicalBytes;
            if (bytes is not > 0) continue;
            var detail = incomplete
                ? "Total working set of an app/host group missing at least one private reading. Some private pages may already be attributed. This observation overlaps other rows and is not subtracted from the remainder."
                : "Working set minus measured private pages. Shared images and mapped files can appear in several processes and in resident system cache. These observations are not additional allocations and do not sum to the remainder.";
            remainder.Children.Add(new MemoryConsumerRow
            {
                Id = "other:observation:" + consumer.Id,
                Name = consumer.Name,
                Summary = consumer.Summary,
                ExecutablePath = consumer.ExecutablePath,
                IconKey = consumer.IconKey,
                ObservedBytes = bytes,
                Metric = incomplete ? "Working set (overlap)" : "Shared WS (overlap)",
                Detail = detail + "\n" + consumer.Detail,
                Processes = consumer.Processes,
                HasConfirmedProcessLinks = consumer.HasConfirmedProcessLinks
            });
        }
    }
    public static MemoryConsumerRow FromWorkload(MemoryWorkload workload)
    {
        var row = new MemoryConsumerRow
        {
            Id = workload.Id,
            Name = workload.Name,
            IconKey = workload.ContainerId is not null || workload.Id == "docker" ? "WbNavApps"
                : workload.Metric == "Process RSS" ? "WbEntityApp" : "WbAdapterVirtual",
            ObservedBytes = workload.Bytes,
            Metric = workload.Metric,
            Detail = workload.Detail,
            ContainerId = workload.ContainerId,
            Summary = workload.Children is { Count: > 0 } ? $"{workload.Children.Count} items" : workload.Summary
        };
        if (workload.Children is not null) row.Children.AddRange(workload.Children.Select(FromWorkload));
        return row;
    }
    private static int Compare(MemoryConsumerRow left, MemoryConsumerRow right)
    {
        var order = (right.PhysicalBytes ?? right.ObservedBytes ?? -1).CompareTo(left.PhysicalBytes ?? left.ObservedBytes ?? -1);
        return order != 0 ? order : StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
    }
}
