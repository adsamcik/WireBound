# Processes page: usage and complete RAM accounting

## Goal

Let someone answer two different questions from one page:

1. How much of this machine's CPU, RAM, and network capacity is in use?
2. Which processes and system components explain that use, and how much remains unattributed?

The first question can reconcile to a complete physical-memory total. The second
may have an explicit remainder. The interface must not imply that the sum of
process rows equals all used RAM.

## Page layout

```text
Processes  200 running                                  Live · updated 14:32:08
CPU 62%                   RAM 18.5 / 31.8 GiB            Network: helper needed

RAM accounting                          32.0 GiB installed · 31.8 GiB usable
[ in use 18.5 ][ standby 9.3 ][ free 4.0 ][ reserved 0.2 ]
In use 58%     Available 42%            View breakdown

Search processes...       Show system       Memory: Resident RAM      Columns
Process                     CPU       Resident RAM     Down       Up
App / process name          12.4%       2.40 GiB       ...       ...
PID 1234                               7.5% of usable RAM
```

- Keep the table's virtualized viewport. The header, summaries, RAM strip, and
  toolbar should remain compact enough that several rows are visible at normal
  window height. Use a responsive layout or horizontal table scrolling at narrow
  widths, while preserving the same column geometry for headers and rows.
- Show one Live state. Put process count and sample time by the page heading;
  remove the process-count summary card and the vague "Sorted live snapshot" line.
- Use three summary groups: whole-machine CPU, physical RAM, and network rates.
  Network collection status and its action belong in the network group. A missing
  helper should not take a separate banner or make dashes look like zero traffic.
- Put machine RAM accounting above the process list. A compact strip is always
  visible; a breakdown expands in place. The strip is organized by physical
  memory **state**, not by process ownership.
- Name the table memory column **Resident RAM** (Windows working set / Linux
  RSS). Show bytes first and optionally `% of usable RAM` beneath. This percent
  is a per-row reference, not an additive share of the machine. Put private
  commit, shared/private resident details, path, and history in a selected-row
  detail view only when the platform can actually supply them.
- Right-align numeric headers and values to matching edges, use tabular digits,
  and keep each process name and PID on the same left edge. Keep status badges
  beside the name. Use subtle usage bars only where the denominator is clear.
- Keep Download and Upload as current rates. Rename `Session` to **Transferred
  since monitoring started** in an optional column or detail panel; it is a
  byte total, not another rate. Grouping by app is a later opt-in view with
  explicit grouping rules; avoid treating all same-named processes as one app.

## RAM model and labels

Two views of the same RAM answer different questions and must stay separate:

| View | Reconciliation rule | What to show |
| --- | --- | --- |
| Physical state | Installed = OS usable + hardware reserved, when installed capacity is measured. OS usable = in use + available. | In use; available, initially as one segment; hardware reserved only when measured. Split available into standby/cache and free only with a source that can do so correctly. |
| In-use attribution | In use = measured non-overlapping categories + Other in use. | Process-private resident, shared resident counted once, kernel/driver allocations, and other categories only when their definitions and coverage are validated. Always expose the remainder. |

For Windows, `GlobalMemoryStatusEx` provides OS-usable physical total and
available bytes; it does not provide installed capacity or a detailed physical
page breakdown. `GetPhysicallyInstalledSystemMemory` can provide installed RAM
from firmware when available. For Linux, `/proc/meminfo` provides `MemTotal`
and `MemAvailable`, but `MemAvailable` is an estimate of reclaimable memory,
not a directly measured standby list. Treat platform-specific splits as such.
Sample all segments for one timestamp and surface stale or incomplete samples.
The current providers' GC fallback is process-scoped; it must not be shown as
whole-machine RAM when an OS read fails.

Do not put process working sets into an additive physical-memory bar: shared
pages are present in more than one process working set, and some resident
allocations are outside process working sets. Windows `PrivateMemorySize64`
represents private commit, not private resident RAM. The current Linux provider
uses `RSS - shared` from `/proc/[pid]/statm`; this is not equivalent to Windows
private commit and must not be given a cross-platform shared label.

When only total/available are known, show a complete two-part **usable RAM**
strip and a status such as "RAM total complete · attribution unavailable".
If installed RAM is unknown, omit reserved rather than claiming it is zero.
With more telemetry, show an attribution coverage figure based on the portion
of in-use bytes that fits validated, non-overlapping categories. Do not call a
remainder "Windows" or "System" merely because its owner is unknown.

## Current implementation and work required

| Area | Current behavior | Design change |
| --- | --- | --- |
| `AppsViewModel.TotalMemory` | Sum of per-process working sets, displayed as `TOTAL MEMORY`. | Remove as machine total. Read system physical used/usable from the existing system monitor, with sample time and unavailable state. |
| `AppsViewModel.TotalCpu` | Sum of sampled process CPU percentages. | Read whole-machine CPU from the system monitor; process rows remain process CPU. |
| Network summary | Sum of per-process rates, or a dash when the per-process monitor is absent. | Source whole-machine rates from network monitoring; distinguish overall rate from per-process coverage. |
| RAM provider | `MemoryInfoData` contains total, used, available, and virtual memory. | Add an optional platform capability for installed capacity and physical-state detail; retain the basic two-part view when unavailable. |
| Process data | Windows working set and private commit; Linux RSS and estimated private resident. | Label resident consistently; show platform-specific secondary metrics with definitions and availability. |
| Table | Repeated header/row column widths, six columns, row name/PID nested with badges. | Share one set of width constants/styles, right-align numeric edges, trim row height, move optional session bytes out of the default view. |

The supplied screenshot labels a filter "Show restricted" and shows a helper
status card, while this checkout's AppsView says "Show system" and has a
generic telemetry status card. Confirm which build is the design baseline
before changing filter semantics. If both filters exist, keep them distinct.

Build this in stages: (1) correct labels and machine totals, plus alignment;
(2) basic usable-RAM strip and drill-down; (3) measured platform-specific
physical-state categories; (4) validated attribution categories and coverage.
Each stage should expose only metrics supported by its provider. Keep process
enumeration page-scoped and the list virtualized.

## Acceptance checks for implementation

- `OS usable = in use + available` for a single sample, with negative and
  inconsistent provider values shown as unavailable rather than clamped into
  a misleading 100% bar.
- If installed capacity is measured, `installed = OS usable + reserved` within
  the provider's documented units/precision. If not measured, the UI says
  "usable RAM" and does not show a reserved segment.
- A sum of process working sets is never labeled total RAM, used RAM, or
  attribution coverage. Private commit never appears as resident RAM.
- Whole-machine and per-process CPU/network sources are labeled separately.
  No network telemetry and an idle measured network rate have distinct states.
- Headers and numeric rows align through live updates, search, sorting, and
  narrow-window scrolling. Keyboard focus and automation names remain usable.
- Restricted or exited processes do not silently become zero-usage entries;
  the status explains incomplete process coverage.

## Technical references

- [MEMORYSTATUSEX and available physical memory](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/ns-sysinfoapi-memorystatusex)
- [GetPhysicallyInstalledSystemMemory](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-getphysicallyinstalledsystemmemory)
- [Windows working sets](https://learn.microsoft.com/en-us/windows/win32/memory/working-set)
- [Linux `/proc/meminfo` definitions](https://www.kernel.org/doc/html/latest/filesystems/proc.html)
- [RAMMap as a physical-page analysis benchmark](https://learn.microsoft.com/en-us/sysinternals/downloads/rammap)
