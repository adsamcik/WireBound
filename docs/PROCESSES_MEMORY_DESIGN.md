# Processes and Memory: connected resource details

## Navigation and purpose

Use **Overview · Processes · Memory · Connections**. Page headings are simply
**Processes** and **Memory**. Processes identifies active workloads and lets
people inspect related processes. Memory explains physical RAM and provides
links into the relevant Processes context. A process row is one observation,
not a claim that every byte of a workload belongs to that process.

The supplied screenshot labels the page Processes; this checkout currently
uses an Apps route and Apps navigation label. Keep the existing route alias
working when the visible label becomes Processes, and add a distinct Memory
route. Preserve any external or saved navigation state.

## Processes

Use one compact line for whole-machine CPU, RAM, and network activity; the
table follows immediately. Put the process count beside its table heading.
Keep search, grouping, and column selection in one restrained toolbar.
Remove repeated Live badges, large metric cards, a helper banner, introductory
subtitles, and per-row start times.

Default to a virtualized hierarchy of recognizable apps and workloads, with
individual processes beneath them. A VM may be a named workload group when
its identity is established. Each process instance occurs once in the
hierarchy. Other relationships appear in its details, not as duplicate rows.

Default columns: **Name · CPU · Resident RAM · Download · Upload**. Headers
and numeric values share the same grid and right edges. PID sits directly
below the name. The RAM column is the process working set/RSS; it is not
additive physical ownership. A VM group must not display a sum of host
working sets as the VM's complete physical footprint. Show an independently
measured VM footprint in details with its own source and denominator.

Selecting a process reveals its precise metrics, path, related workloads,
relationship evidence, and a link to the relevant Memory allocation when
one exists. Missing network collection affects only network cells and their
details. The page remains useful without the helper.

## Memory

Start with a compact physical capacity strip: **In use · Available · Hardware
reserved**, with the installed total only when measured. The allocation
consumer list occupies the main viewport. Apps, VM hosts, named guests,
individual containers, kernel allocations, and shared/unattributed RAM appear
together without broad category headers or duplicate category subtotals.

Expand an app or container only to inspect its process details. Named guests
and containers are directly visible. System entries identify measured
allocation types, such as nonpaged pool, rather than repeat a category total.
An allocation detail holds its definition, source,
  sample age, and evidence status. Use a separate **View related processes**
  action when links exist; **Check process links** can lead to a contextual
  empty state when correlation is supported but found none. Merely selecting
  or expanding a row stays on Memory.

Use one virtualized, expandable **Memory consumers** list for applications,
virtual machines and containers, system allocations, and shared/other RAM.
Keep the capacity strip compact and give the list the remaining viewport.
Its proportional bar shows the largest measured app groups and VM hosts,
smaller-consumer buckets, system/shared RAM, available RAM, and hardware
reservation. Multiprocess apps remain one slice. Hover exposes bytes, share,
and measurement scope; activating a consumer slice clears the list filter,
expands its group, and reveals its current row. Colors stay stable when
visible consumers change rank. Overlapping observations never enter the bar;
incompatible attribution falls back to total in-use RAM. Smaller-consumer
slices reveal their largest remaining member; system slices reveal the
measured allocation directly.
Application groups use the exact executable path (process name fallback),
and expand to individual process instances. Docker's aggregate wrapper is
omitted when its individual containers are available.
Search includes descendants and PIDs, revealing matching ancestry without
changing saved expansion state. Refresh preserves filters and expansion.

The **RAM** column is sorted descending across all top-level consumers;
optional process details sort within their owner. Use complete measured
private/system RAM when available, otherwise the reported working-set, guest,
or container value. This prevents partially measured app groups from hiding
large unmeasured processes. Unknown values sort last. The **Measurement**
label states the source metric; list values can overlap and are not additive.
Both physical and observed values, including partial private measurements,
remain in row details. Windows uses private working-set
counters on supported Windows 11 builds; Linux reads private resident pages
from smaps_rollup only for Memory captures. Committed memory is never used
as a substitute for physical RAM. Inaccessible or unsupported measurements
remain unavailable.

Assigned VM memory, Docker container usage, and container-process RSS retain
their exact metric labels. These observations can overlap physical RAM and do
not enter the physical allocation equation. Named Hyper-V guests and running
WSL distributions are read without starting or entering a guest. Local
libvirt system/session connections are read-only; remote default connections
are excluded. WSL distribution RAM cannot be isolated from the utility VM.

Docker uses only the local socket endpoint from the active CLI context.
Each running container reports Docker stats usage (Linux inactive file cache
excluded). Expanding a container lazily reads docker top PID/RSS/command;
process RSS may overlap and does not sum to container usage. Unsupported
engines, missing tools, denied access, and stopped workloads have concise
unavailable rows. No in-container or in-guest command is executed.

Physical state and ownership are separate equations:

| View | Reconciliation |
| --- | --- |
| Capacity | Installed = OS usable + reserved, if installed is measured. OS usable = in use + available. |
| Allocation | In use = exclusive physical categories + Unattributed. |

Shared and other remains visible with its reason. If samples or definitions are
incompatible, show the breakdown as unavailable instead of clamping a
remainder into a false 100% explanation.

Detailed Memory samples now include additional resident system allocations:
Windows uses language-neutral PDH gauges for resident paged pool, system cache,
kernel/driver code, and modified pages awaiting writeback. Standby is excluded
because it is already Available. Linux reports unreclaimable slab, kernel stacks,
and page tables from `/proc/meminfo`. These extra readings are requested only on
the Memory page; ordinary system polling stays lightweight. An overfull set of
additional gauges remains an explicitly unreconciled observation, preserving
valid private readings and the original remainder rather than forcing a fit.

Windows process sampling uses a validated system-wide process snapshot so
protected processes, Secure System, and Memory Compression can contribute their
private resident pages without opening their memory. PID plus kernel creation
time identifies each sample; executable enrichment requires a matching creation
time. Invalid native layouts fall back to the existing per-process APIs.

Expanding Shared and other shows per-app/host shared-working-set observations
and working sets lacking private readings. These details can overlap each other
and the resident system cache, never add to physical totals, and do not claim to
partition the remainder. Ordinary app searches exclude these diagnostic copies
unless the remainder has been expanded. The unresolved physical remainder stays
visible: unique shared-page ownership, driver-locked pages, and some guest
allocations need information not exposed by these counters.

## Memory to Processes workflow

A named VM or another linkable allocation has an explicit **View related
processes** action. This opens Processes with a small persistent context:

> Windows development VM · Related processes
> Back to Memory · Clear

The context is a typed relationship filter, not a text search. It carries a
stable workload identity, allocation identity, source snapshot, and a set of
process-instance identities. A process instance includes PID and start time
or an equivalent generation marker; PID or name alone can be reused. The
relationship provider refreshes links as processes start or exit. If the
workload stops, keep its identity and show **Stopped**.

Exact application/process links appear by default and reject recycled PIDs.
VM host name matches retain their separate likely-link opt-in.
Confirmed relationships appear by default. **Include likely matches** is
an optional control; likely rows carry a visible Likely label and evidence
in details. Ordinary text search still works inside the filtered context.
The machine usage strip remains machine-wide. Any contextual total names
its metric and scope; never substitute a sum of process working sets for
the VM's physical allocation.

For partial coverage, show known related processes and say **Some allocations
have no process link** in the context detail. If none are linked, show
**No process attribution available** with the selected allocation's value
and Back to Memory; do not send the user to an unfiltered generic list.
Do not compute an unlinked byte count by subtracting incompatible metrics.
Back restores Memory's expansion, selection, and scroll position. Clear
removes only the relationship filter.

Relationship confidence and memory measurement confidence are independent:

| Relationship | UI behavior |
| --- | --- |
| Confirmed | Provider explicitly links this process instance to the workload; default result. |
| Likely | A heuristic suggests a link; opt-in result with a Likely label. |
| Unlinked | No process link; no fabricated row. |

A confirmed relationship does not prove that all of a process's RAM belongs
exclusively to the VM. Its link evidence and memory metric definition remain
available in details.

## Current implementation and delivery

The current Apps view sums process working sets into TOTAL MEMORY and process
CPU samples into TOTAL CPU. The current memory providers expose only total,
used, and available; there is no VM provider or stable process start identity
in the process snapshot. Windows and Linux GC fallbacks are process-scoped,
so an OS read failure must not become a machine total.

1. Keep the Processes page and correct machine versus process labels. Share
   header and row column geometry; preserve its virtualized, page-scoped
   sampling.
2. Add Memory as a separate route with the capacity strip and truthful
   incomplete-coverage state.
3. Add read-only platform workload discovery, stable process-instance IDs,
   relationship evidence, and typed Memory-to-Processes navigation. Start
   with providers whose links can be verified; leave unsupported VM types
   visible as precisely labeled observations.
4. Add exclusive physical allocation categories only after overlap and
   same-sample reconciliation tests. Offer expensive per-page or trace
   diagnostics on demand, outside the two-second live poll.

Platform capabilities require Abstract, Windows, Linux, and Stub contracts.
The data model needs source, timestamp, denominator, measurement evidence,
relationship evidence, stable workload ID, stable process instance ID, and
whether a byte figure is additive. New interactive elements need automation
names and keyboard access.

## Implemented baseline (2026-09-24)

- Navigation now has Processes and Memory; the old Apps route remains valid.
- Processes uses machine-wide CPU, RAM and network readings, and its default
  table aligns Name, CPU, Resident RAM, Download and Upload. Selecting a row
  reveals its path, private commit and resident RAM.
- Memory reconciles usable physical RAM as in use plus available. Windows also
  measures installed RAM and nonpaged kernel pool; the remaining in-use bytes
  stay explicitly Unattributed. A failed OS read never falls back to the
  WireBound process's GC memory as if it were machine RAM.
- Windows and Linux recognize selected VM host processes as **observations**.
  These name-based links are Likely and require opt-in in the contextual
  Processes view. Process identity includes PID and OS start marker, so a
  recycled PID does not inherit a link. The observed working set stays outside
  the additive physical ledger.

Named VM discovery, independently measured guest physical allocations, and
exclusive physical ownership for most in-use pages remain unsupported by the
current platform providers. The page shows that gap as Unattributed; it must
not turn a process working set into a claim of complete VM RAM ownership.

## Acceptance checks

- Processes and Memory are both reachable in navigation; returning from a
  filtered process view restores the exact Memory context.
- A confirmed VM link resolves by workload and process instance identity.
  A likely match does not silently enter the default result. A process
  appears once in the hierarchy.
- System allocations without a process link show a contextual empty state.
  Partly linked categories disclose incomplete coverage without invented
  byte arithmetic.
- Physical totals reconcile from compatible samples; assigned VM memory,
  host process RAM, and host physical allocation remain distinct.
- Tables stay aligned through sorting, filtering, expansion, live refresh,
  keyboard navigation, and narrow-window scrolling.

## References

- [Windows physical memory totals](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/ns-sysinfoapi-memorystatusex)
- [Installed versus OS-usable RAM](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-getphysicallyinstalledsystemmemory)
- [Windows working-set behavior](https://learn.microsoft.com/en-us/windows/win32/memory/working-set)
- [Hyper-V Dynamic Memory](https://learn.microsoft.com/en-us/windows-server/virtualization/hyper-v/dynamic-memory)
- [WSL memory settings](https://learn.microsoft.com/en-us/windows/wsl/wsl-config)
- [RAMMap physical-memory views](https://learn.microsoft.com/en-us/sysinternals/downloads/rammap)
