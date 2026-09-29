# WireBound unified dashboard redesign

Status: proposed product direction
Supersedes the route-led information architecture in `DESIGN_UNIFIED_MONITORING.md`.

## Why the current UI feels fragmented

WireBound's navigation exposes the order in which features were added: Overview, Live Chart,
Apps, Connections, System, and History each behave like separate products. The same resource
appears in several places, while moving from a symptom to its cause requires changing pages and
rebuilding context.

The redesign should organize the product around one question:

> What is this system doing, and what is responsible for it?

Network, CPU, memory, and disk are peer resources. Processes and connections explain resource
activity. Time is a mode applied to both, not a separate data silo.

## Product model

Use three levels of progressive disclosure:

1. **Glance — Dashboard:** current resource state, recent activity, and the top contributors.
2. **Explain — Resource drill-down:** one resource over time, its capacity or throughput, and the
   processes or connections responsible.
3. **Investigate — Entity detail:** one process, connection, adapter, or historical interval across
   all relevant resources.

This is a single analytical canvas with contextual states, not seven independent pages.

## Fluent 2 refinement

The redesign follows Fluent 2 as a system rather than borrowing only its rounded rectangles:

- Use a four-pixel spacing foundation and consistent 8, 12, 16, 24, and 32 px rhythm.
- Use Segoe UI Variable and a restrained Fluent type ramp; large type is reserved for the page title
  and the currently important value.
- Use neutral semantic surface, foreground, stroke, shadow, and brand tokens so light, dark, and
  high-contrast themes remain possible. Resource colors identify data series, not card chrome.
- Use strokes for most Windows surfaces and elevation only when an element actually floats. The
  bottom navigation cards and view-filter surface are elevated; ordinary content regions are not.
- Prefer spacing and alignment over nested cards, borders, glows, and tinted icon boxes.
- Use a card only for a single concept with one obvious action. Compact resource-selector tiles
  qualify; the timeline and tables are content regions rather than additional cards.
- Every state has rest, hover, pressed, selected, keyboard-focus, disabled, loading, empty, error,
  and unavailable-platform behavior.

### Element test

Every visible element must pass at least one of these tests:

1. It communicates current state.
2. It helps explain a change or anomaly.
3. It changes scope or time.
4. It starts a common investigation.
5. It is required for navigation, accessibility, or window behavior.

If an element does none of these, remove it. If two elements pass for the same reason, keep the one
with the clearer interaction.

## Chart contract

Charts must answer a question. Movement alone is not information.

## Resource focus model

The dashboard is not always network-first. It has an explicit focus state:

`Automatic | Network | CPU | Memory | Disk`

The resource selector replaces the large summary-card row. Each option shows only the resource name,
current value, and exceptional state. Selecting it changes the chart, contributor ranking, contextual
filters, and primary action. `Automatic` chooses the resource involved in the most relevant recent
event and shows only correlated context.

This gives every resource equal treatment without turning the dashboard into four simultaneous
detail pages.

### Focus-specific questions

| Focus | Primary question | Supported signals | Attribution |
| --- | --- | --- | --- |
| Automatic | What deserves attention now? | Relevant resource plus correlated context | Process or system cause when known |
| Network | What is using bandwidth? | Download, upload, adapter, local/network scope | Processes and connections |
| CPU | What is consuming compute? | Total, per-core, frequency, temperature when available | Processes by CPU |
| Memory | What is consuming capacity or creating pressure? | Used, available, virtual/swap when supported, pressure thresholds/events | Processes by private memory |
| Disk | What is keeping storage busy? | Read, write, activity percentage | Processes when I/O attribution exists |

Do not offer a drive selector, latency, queue depth, commit, or another metric until the platform
providers actually supply it. Unavailable optional signals remain absent rather than disabled clutter.

### Focus behavior

- Selecting a resource does not navigate away; it refocuses the analytical canvas.
- The selected resource gets the dominant chart. Correlated resources appear as smaller context lanes
  only when they help explain the selected interval.
- `Open details` moves to the full resource drill-down while preserving time and filters.
- Returning to Overview restores the selected resource and chart interval.
- The last manual focus persists for the session; `Automatic` is opt-in again after manual selection.

## Filter architecture

Filters use progressive disclosure and appear only at the level where they apply.

### Always visible

- **Resource focus:** Automatic, Network, CPU, Memory, Disk, in the top resource selector.
- **Time:** Live, 5 min, 1 hour, Today, Custom, in the bottom-right view controls.

### Contextual chart controls

- **Network:** Download, Upload, Both; adapter; all/network/local scope.
- **CPU:** Total or per-core; frequency and temperature only when available.
- **Memory:** Used, Available, or Swap/Page file when supported; pressure-event markers.
- **Disk:** Read, Write, Both, or Activity percentage.

### Contributor controls

- Search by process or application.
- All, user, or system processes.
- Sort follows the focused metric by default.

### Advanced popover

- Compare with previous equivalent period.
- Show/hide normal band and event markers.
- Sampling granularity when the selected time range supports it.

Do not show all controls simultaneously. The default view contains time, focus, and at most two
contextual controls. Active advanced filters appear as a count on one `Filters` button.

## Information ownership and deduplication

Each fact has one visual owner:

| Information | Owner | Must not be repeated in |
| --- | --- | --- |
| Current resource value | Resource selector | Chart title and cause panel |
| Change, peak, baseline, threshold | Chart and its annotation | Resource selector |
| Selected timestamp/interval | Shared crosshair and cause-panel heading | Page-level observation |
| Responsible process or connection | Cause panel | Resource selector |
| Application navigation | Bottom-left navigation cards | Page header |
| Time range | Bottom-right filter surface | Individual panels unless overridden |

Remove the page-level generated observation while a chart selection and cause panel are visible. It
is useful only as an empty-selection summary in Automatic mode. The selected timestamp may appear at
the crosshair and in the cause-panel heading because it is the explicit link between those regions.

### Resource summaries

Do not use unlabeled sparklines. Each resource-selector option contains:

- the current value and unit;
- an exceptional-state indicator only when action or attention is warranted.

Comparisons belong to the chart and attribution belongs to the cause panel. This compact ownership
keeps the selector useful without repeating the analysis below it.

### Activity and causes

Replace the generic correlated-lines panel with synchronized resource lanes that share time but keep
their real units. The visualization includes:

- a labeled y-range for every lane;
- a subtle normal band or capacity reference when one is meaningful;
- a selected-time crosshair shared by all lanes;
- peak, threshold, and anomaly markers only when an event warrants them;
- one concise generated observation, for example: `Network peaked at 68 Mbps at 14:32; Teams.exe
  accounted for 72%`;
- a linked contributor list that updates for the selected point or interval.

The chart defaults to the resources that explain the current event. `All resources` remains
available, but four flat lines should never be shown merely to fill space.

### Interaction

- Hover previews exact values; click or keyboard selection pins a time.
- Drag selects an interval and re-ranks the contributor list.
- Double-click or Enter opens the selected resource drill-down.
- Escape clears the selection before navigating back.
- Live mode keeps the latest sample in view until the user pins or pans, then clearly shows that the
  chart is paused from following live data.
- Tooltips, labels, line shapes, and accessible summaries ensure color is never the only identifier.

## Application shell

### Top bar

- WireBound identity, monitoring state, pause/resume, settings, and native window controls.
- Do not show a full-width machine selector while WireBound monitors only the local machine.
- Keep the adapter selector inside Network context; it is not a global application scope.
- Show healthy status quietly in the title bar. Promote only actionable warnings into the canvas.
- Window-level actions only; page titles and analytical controls stay in the canvas.

### Bottom action row

Anchor two deliberately separate groups to the bottom content margins. The left group contains three
independent, low-elevation Fluent navigation cards with equal height and consistent gaps:

1. **Overview** — returns to the unified resource dashboard.
2. **Processes** — all running processes across CPU, memory, and network.
3. **Connections** — active local and remote connections.

Each destination owns its full card hit target and selected, hover, pressed, and focus states.
`Overview` uses a subtle accent-tinted selected treatment; inactive cards remain neutral. The cards
must not be wrapped in another enclosing navigation surface.

One separate compact surface on the right contains the view scope: time range, the focused
resource's primary signal, contributor scope, and one `Filters` button. These controls remain grouped
because they jointly define one analytical view. A flexible spacer separates the navigation-card
cluster from this filter surface.

Settings belongs in the top bar because it configures the application rather than representing a
monitoring destination. Timeline is a time mode shared by every resource and entity, so it belongs
in the right-side time control rather than in navigation.

Resource names do not belong in navigation. Network, CPU, memory, and disk are entered from their
dashboard summaries because they are drill-downs rather than separate application areas. The bottom
action row participates in layout and never overlaps content.

## Overview dashboard

### 1. Resource context

- Do not repeat an **Overview** page title while the selected bottom navigation item already identifies
  the destination.
- Begin the canvas with the resource selector so the highest content position carries current state
  and changes analytical focus.
- One compact observation appears only when it communicates a useful change, cause, or warning; place
  it inside the focused analysis rather than reserving a page-header band.

### 2. Resource selector

Show one compact responsive row with Automatic and four equal resource peers:

- **Automatic:** the most relevant current event, or `No unusual activity`.
- **Network:** current download and upload.
- **CPU:** current total utilization.
- **Memory:** current used/total and pressure only when exceptional.
- **Disk:** current read/write or activity percentage.

Each tile is one accessible selection button. It focuses the canvas without navigation and carries
the selected time range with it. `Open details` in the focused canvas launches the drill-down. Stable
resource colors identify charts, but text and icons must also convey meaning.

### 3. Activity and causes

Use the chart contract above. The dominant region combines synchronized lanes with a linked cause
panel. This avoids pretending that Mbps and percentages share a meaningful scale while making
spikes and their responsible processes easy to correlate.

### 4. Top activity

The linked process list is ranked for the selected chart point or interval rather than duplicating a
generic leaderboard:

`Process | CPU | Memory | Download | Upload | Connections`

Default sorting follows the active resource or selected chart interval. A row opens a process detail
with its resource history and connections. If no chart selection exists, use the most recent minute.

### 5. Exceptions

Do not reserve permanent space for a context snapshot. Show a Fluent InfoBar only when WireBound has
an actionable warning, limited-platform capability, stale data, or an elevated-helper requirement.

## Resource drill-down template

Every resource uses the same structure so the UI feels learned after the first visit:

1. Breadcrumb/back action: `Overview / Resource`.
2. Current value, capacity or throughput, and comparison with the selected interval.
3. Full-width history visualization using resource-appropriate units.
4. Breakdown by responsible process, adapter, drive, core, or connection.
5. Related actions such as **View connections** or **Open process**.

The bottom action row remains visible. Back/Escape returns to the previous analytical state and
restores scroll position, filters, and time range.

## Mapping from the current product

| Current route | New location |
| --- | --- |
| Overview | Overview dashboard |
| Live Chart | Dashboard timeline and resource drill-down charts |
| System | CPU, Memory, and Disk resource drill-downs |
| Apps | Processes navigation destination plus process detail |
| Connections | Connections navigation destination plus connection detail |
| History | Timeline mode, preserving the selected resource/entity context |
| Settings | Top-bar application action |

The existing views can remain temporarily during migration, but the new shell should stop presenting
them as peer navigation routes.

## Window behavior

- **Wide window:** one-row resource selector; activity timeline beside a compact cause panel.
- **Medium window:** wrapping resource selector; timeline and cause panel stack.
- **Narrow window:** horizontally scrollable resource selector with clear edge affordance; the three
  navigation cards remain visible while the right filter surface collapses into one labeled `View`
  button.
- Keep dashboard scroll position stable when a live sample arrives.
- Never use color alone for warnings, selected state, or chart identity.

## Delivery sequence

### Phase 1 — New shell and dashboard composition

- Replace the 240 px navigation rail with the top bar and bottom action row.
- Compose the existing Overview, System, Apps, and Connections data into dashboard summaries.
- Introduce an explicit dashboard/drill-down navigation state separate from legacy routes.

### Phase 2 — Shared drill-down framework

- Add a reusable resource-detail host and breadcrumb/back behavior.
- Move existing live charts into Network, CPU, Memory, and Disk resource details.
- Preserve time range and selection when moving between summary and detail.

### Phase 3 — Entity investigations and timeline mode

- Add process and connection detail states.
- Fold History into the shared Timeline mode.
- Remove legacy Charts and System routes after feature parity is verified.

## Acceptance criteria

- A user can identify the busiest resource and responsible process without changing top-level pages.
- Any resource can be focused with one action and its detail reached with one additional action.
- Any process or connection detail is reachable with at most two actions from Overview.
- Live and historical views preserve the same resource/entity context.
- The bottom action row never covers content at the minimum supported window size.
- All interactive elements have accessible names, visible focus, and keyboard behavior.
- Existing network, system, process, connection, and history capabilities remain reachable during
  migration.

## Decisions to validate in the prototype

- Whether the default timeline should show all four synchronized lanes or only the two most active.
- Whether Disk belongs in the first release of the resource selector on platforms with limited data.
- The breakpoint where the right filter group collapses into one `View` button.
