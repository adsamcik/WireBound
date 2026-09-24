# Memory detail page

## Decision

Rename the Processes destination **Memory** and title the page **What's using RAM**. It should explain occupied physical RAM by application, virtual machine, system allocation, and any amount still unattributed. The process list becomes an Applications drill-down.

The first screen should answer three questions: how much RAM is occupied, which consumers account for it, and what remains unexplained. Remove CPU/network cards, process count, the helper banner, large icons, and the permanent full-width search toolbar. Those compete with the memory question.

## Page structure

1. **Capacity:** one compact line with RAM in use, OS-usable total, available, and installed total when measured. A quiet strip shows in-use and available; hardware reserved appears only when installed capacity is measured. Detailed memory states are disclosed separately.
2. **Allocation ledger:** the main surface. Show a short descending tree of measured non-overlapping categories, each with used RAM and percent of *in-use RAM*. Candidate rows are Applications, Virtual machines, System and drivers, Shared allocations, and Unattributed. Show only rows supported by a validated provider.
3. **Drill-down:** expand Applications to app groups and processes; Virtual machines to named host workloads; System and drivers to meaningful, exclusive allocation types such as nonpaged pool, driver-locked pages, and page tables when measured. Expand a child for its meaning, source, sample time, and caveats. Keep those details in place so the page does not reserve space for an empty detail pane.

Use one shared numeric grid, tabular digits, subtle separators, and right-aligned values. Keep process virtualization and page-scoped collection. Do not move rows under the pointer during live updates. Search appears when the expanded tree is large enough to need it.

## Accounting contract

The capacity strip describes *state*: installed = OS usable + reserved, when installed is measured; OS usable = in use + available. The ledger describes *ownership*: in use = exclusive measured categories + Unattributed. These are separate views. Available can include reclaimable cache, so it is not necessarily empty.

Every parent must equal its measured children plus an explicit remainder. If values overlap, were sampled at incompatible times, or have uncertain definitions, show **Breakdown unavailable** with the reason. Do not clamp a negative remainder to zero or claim 100% explained because subtraction filled the final row.

Use three evidence treatments: **Measured** (specific source and time in detail), **Estimated** (visible label and method; outside an exact ledger), and **Unavailable** (dash and reason, never zero). Unattributed means the RAM is included in the machine total but its owner is not known. Expanding that row explains the current coverage gap and shows an applicable collection or diagnostic action only when that capability exists.

## Virtual machines

Make VMs first-class named workloads. Distinguish configured capacity, memory assigned to the guest, guest-reported usage, host-process working set, and *host physical RAM attributable to the workload*. Only validated non-overlapping host physical bytes may enter the ownership ledger.

- **Hyper-V:** discover VM identity using a native provider. Assigned and demand figures are useful detail, but are not automatically host resident RAM.
- **WSL 2:** show the utility VM as one workload unless a provider can measure separate host physical usage per distribution. Running distribution names are context, not invented memory attribution. Memory reclaim can alter host allocation independently of guest demand.
- **Other hypervisors and containers:** use provider-specific identities when available. If only a recognizable host process is measurable, label its working set as a host-process observation, not the complete VM footprint.

When only assigned or process-resident bytes are available, show the VM under **Observed consumers** with that exact metric; keep it outside the additive ledger. Never count VM-owned bytes a second time under Applications or System.

## System and process limits

Kernel pools, driver allocations, page tables, compression, shared pages, and file-backed allocations can explain RAM that a process list omits. Their counters may overlap each other or process working sets; each needs a documented definition and reconciliation test before it enters the ledger. A sum of process working sets is not a physical RAM total. Private commit is not private resident RAM.

The current Apps view labels a sum of process working sets TOTAL MEMORY and sums process CPU samples as TOTAL CPU. The current memory providers expose only total, used, and available; there is no VM attribution provider. Their GC fallback is process-scoped, so an OS read failure must not produce a machine-total display.

## Delivery sequence

1. Correct the page name and whole-machine RAM source; introduce the compact capacity strip. Keep current process values in the Applications drill-down with honest labels.
2. Add optional read-only VM discovery and metric provenance in platform providers. Show named VMs with precisely labeled observations before claiming physical attribution.
3. Add exclusive physical ownership categories after provider-level overlap and timing tests. Populate the ledger incrementally and retain Unattributed.
4. Offer expensive per-page or trace diagnostics on demand, outside the two-second live poll.

The data contract needs bytes, denominator, timestamp, provider, evidence type, stable owner identity, and whether a figure is exclusive and additive. Optional platform capabilities need Windows, Linux, and Stub implementations under Platform.Abstract.

## Acceptance checks

- Capacity values reconcile from one compatible sample. Missing installed RAM does not imply zero reserved.
- Ownership parents equal exclusive children plus a remainder. A VM and its host process never appear twice.
- Assigned VM RAM, guest usage, process working set, and host physical use have distinct code and UI labels.
- Missing, stale, restricted, estimated, and true-zero readings look different.
- The largest identified consumer and unexplained amount are visible without expanding anything. Numeric columns stay aligned and keyboard accessible.

## References

- [Windows physical totals and availability](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/ns-sysinfoapi-memorystatusex)
- [Installed versus usable memory](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-getphysicallyinstalledsystemmemory)
- [Windows working sets](https://learn.microsoft.com/en-us/windows/win32/memory/working-set)
- [Windows kernel and cache counters](https://learn.microsoft.com/en-us/windows/win32/api/psapi/ns-psapi-performance_information)
- [Hyper-V Dynamic Memory](https://learn.microsoft.com/en-us/windows-server/virtualization/hyper-v/dynamic-memory)
- [WSL memory settings and reclamation](https://learn.microsoft.com/en-us/windows/wsl/wsl-config)
- [RAMMap physical memory views](https://learn.microsoft.com/en-us/sysinternals/downloads/rammap)
