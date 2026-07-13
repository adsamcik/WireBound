# WireBound Icon System Audit & Replacement Brief

Source of truth for this brief:
- live UI in `src/WireBound.Avalonia/Views/*.axaml`
- icon strings in `src/WireBound.Avalonia/ViewModels/*.cs`
- theme tokens in `src/WireBound.Avalonia/Styles/Colors.axaml` and `src/WireBound.Avalonia/Styles/Styles.axaml`
- branding concepts in `design/icon-concepts/*` and `src/WireBound.Avalonia/Assets/wirebound-*.png`

Design docs mention older or planned surfaces such as `History`; the current codebase was used as the primary inventory source.

## A. App Understanding

### Product purpose
WireBound is a privacy-first desktop monitor for live network traffic and local system health. It combines:
- real-time download and upload monitoring
- per-app and per-connection inspection
- CPU and RAM awareness
- helper-assisted deep tracking
- zero-cloud, local-only data retention

Main workflows in the current app:
- watch the `Overview` page for live network and system health
- inspect the `Live Chart` page for time-based traffic behavior
- drill into `Apps` for per-process usage and destinations
- inspect `Connections` for remote hosts and byte counts
- review `System` for live metrics, historical trends, and correlations
- manage adapters, helper state, alerts, and updates in `Settings`

### Target user
Primary users are privacy-conscious power users, developers, IT admins, gamers, and technically comfortable desktop users who want local observability without SaaS telemetry. The UI assumes users are comfortable with rates, adapters, helpers, filters, and time ranges.

### Visual style summary
- Tone: technical, trustworthy, calm, premium utility
- Palette: deep navy and ocean-blue surfaces with cyan and orange as traffic accents; blue, purple, green, amber, and coral for metric and state colors
- Surfaces: rounded glass-like cards with 12 to 20 px radii, subtle borders, and soft shadows
- Density: data-heavy but not cramped; strong emphasis on large numerals and compact metadata rows
- Typography: Windows-native modern UI feel with bold hero metrics and muted captions
- Motion and metaphor: the docs consistently point toward wires, nodes, and flowing signals
- Platform fit: more Windows-11-like than Linux-native, but intentionally cross-platform rather than platform-chameleonic

### Recommended icon design direction
Use a custom icon language built around a single idea:

`Wire Trace`

The system should feel like instrumentation drawn from:
- cable traces
- terminal nodes
- signal lanes
- rounded PCB/chip geometry

This direction fits the product better than generic charts, globes, gears, or emoji because it:
- reinforces the app name
- matches the cyan/orange network-flow branding already explored in `design/icon-concepts`
- keeps the product technical without looking enterprise-boring
- gives nav, metrics, and helper states one coherent geometry instead of mixed emoji semantics

Recommended personality:
- technical
- restrained
- readable
- slightly premium
- custom but not ornamental

## B. Icon Inventory

| Current icon / asset | Location | Current meaning | Usage context | Replace / redesign / keep / merge | Notes |
|---|---|---|---|---|---|
| `wirebound-*.png`, `design/icon-concepts/*` | window icon, tray icon, installers, concept folder | app brand | branding | Redesign, keep direction | The cable/node silhouette is the strongest existing visual asset. Keep the idea, simplify the geometry, and unify it with the rest of the set. |
| `📊` | Overview session stats, Charts averages, Apps section header, Settings dashboard customization, Overview nav | stats, analytics, dashboard | nav, card header, metric | Split | One glyph currently stands for overview, averages, analytics, and dashboard customization. Those meanings should become separate icons. |
| `📈` | `Live Chart` nav, System peak memory | line chart, peak | nav, metric | Split | The nav icon should communicate live time-series behavior; peak memory should not reuse the same metaphor. |
| `📦` | `Apps` nav, app count card, generic app placeholder in list and detail, apps empty state | applications | nav, metric, placeholder, empty state | Redesign | Package-box semantics are weak for desktop processes. Use an app tile or process-grid metaphor instead. |
| `🔗` | `Connections` nav, `Correlations` tab, Bluetooth tether adapter, correlation empty state | connection, relation | nav, tab, adapter, empty state | Split | Real network connections and abstract metric correlations need related but distinct icons. |
| `💻` / `🖥️` | `System` nav, `System Trends` tab, CPU section, System Health card, Application Settings section, virtual adapter fallback | computer, system, desktop | nav, hardware, settings, adapter | Split | Overloaded. System page, CPU, application behavior, and virtual adapters should each resolve differently. |
| `⚙️` | `Settings` nav, advanced-adapters toggle, columns menu | settings, options | nav, action | Split | A top-level settings icon should not also represent table columns or advanced adapter visibility. |
| `📡`, `📶`, `🔌`, `🔐`, `📱`, `🚇`, `🌐`, `🔄` | adapter selector, adapter display items, overview secondary adapters | adapter types and auto-detect | selector, inline status | Redesign as family | Adapter icons are a real subsystem. They need a shared family with stable metaphors and variant states. |
| `↓`, `⬇️` | Overview, Charts, Apps, Connections | download, received | metric, section, footer | Redesign, keep meaning | Semantics are correct. Replace text arrows with vector arrows matched to the brand language. |
| `↑`, `⬆️` | Overview, Charts, Apps, Connections | upload, sent | metric, section, footer | Redesign, keep meaning | Same as download. Pair as a formal family. |
| `↑`, `↓`, `→`, `○`, `●`, `▲`, `▼` | Overview trend indicators, Apps sort glyphs | rise, fall, stable, idle, sort | state, UI primitive | Keep as primitives or redesign as a mini set | These are closer to UI symbols than branded feature icons. Keep them simple and aligned to the icon system. |
| `🔄` | auto-switch banner, Connections refresh, auto adapter label | auto, refresh, switching | banner, action, selector | Split | Refresh and auto-detect are currently visually identical even though their behavior is different. |
| `ℹ️` | Apps tracking banner | information | status banner | Merge | Should become part of a standard info/warning/error family. |
| `🔍` | Connections search, apps filtered-empty state | search, no results | input, empty state | Redesign | Strong meaning, but the visual language should match the rest of the set. |
| `🔥` | top CPU app | hottest CPU consumer | metric card | Redesign | Too playful for the product tone. Use a CPU-load metaphor instead. |
| `🧠` | top RAM app, memory alerts | memory | metric, settings section | Merge | Stronger than `💾` semantically for “memory pressure,” but it still clashes with the system family. Replace with a custom memory glyph. |
| `⏱️` | chart time range selector | time window | control | Redesign | Replace with a cleaner time-range or history-window glyph. |
| `💡` | chart hint, insights settings | hint, insight | helper copy, section header | Split | A chart hint and an insights/analytics section should be related but not identical. |
| `⚠️` | helper requirements, startup warnings, platform warnings, system error, tamper warning | warning | inline warning, banner, empty state | Merge | Use one warning family with severity color, not mixed emoji text. |
| `❌` | Connections error banner | error | banner | Merge | Standard error family with warning/info/success. |
| `🔬` | byte tracking preview banner | preview, diagnostic, limited measurement | banner | Redesign | Useful meaning but semantically niche. A probe/sampler metaphor would fit the product better. |
| `🛡️` | Elevated Helper section and start action | trusted helper, elevated mode | section header, CTA | Redesign | Keep the idea of protection/trust, but integrate it into the cable/node geometry. |
| `⏹` | stop helper | stop helper service | CTA | Redesign or standardize | This can be a standard stop-square primitive, not a decorative icon. |
| `🔢` | CPU core count | processor cores | CPU metadata | Redesign | Use a core-matrix metaphor instead of “numbers.” |
| `🌡️` | CPU temperature | thermal sensor | CPU metadata | Redesign | Replace with a small thermometer/sensor icon. |
| `🔲` | average CPU card, CPU-to-memory correlation | average CPU, relation | metric, relation | Replace | This icon is semantically weak and should not survive the redesign. |
| `🌐` | Active Connections section, network correlations, top-destinations empty state, generic adapters | network, remote host, internet | section, relation, placeholder, adapter | Split | Globe semantics are generic. Use a network node or remote-endpoint metaphor instead. |
| `🔌` | Ethernet adapter, no-connections empty state | wired connection, disconnected state | adapter, empty state | Merge | Ethernet and disconnected-state should share geometry but differ in state treatment. |
| update badge dot | Settings nav | update available | nav status | Keep | This works as a primitive. Keep the dot, align color and size rules. |
| status dot / gauge dots | main footer monitoring state, `SystemHealthStrip` | monitoring active, metric presence | status primitive | Keep | Keep as primitives rather than replacing them with pictograms. |
| `←`, `→` | apps back button, date-range separator | navigation direction | action, separator | Keep as UI primitives | These should stay minimal and non-branded. Align weight and spacing with the icon system. |

## C. Icon System Specification

### System name
`Wire Trace`

### Grid
- Master grid: `24 x 24`
- Live drawing area: `20 x 20`
- Small variant grid: `16 x 16`
- Large decorative or empty-state icons: `32 x 32` and `48 x 48`
- Brand/app icon tile: `128, 256, 512` plus a simplified `16` tray version

### Stroke
- `24 px` icons: `2.0 px`
- `20 px` icons: `1.75 px`
- `16 px` icons: `1.5 px`
- Cap and join style: rounded
- Export recommendation: expand strokes before shipping SVGs so renderer differences do not change weight

### Corners
- Internal corner radius target: `2 px`
- Outer contour radius target: `4 px`
- Terminal rings and nodes: round, optically overshot by `0.5 px`

### Fill / outline approach
- Outline-first system
- Allow one filled node, plug end, or status core per icon
- Avoid heavy filled silhouettes except for:
  - app brand tile
  - state dots
  - tiny small-size compensations

### Negative space usage
- Minimum internal gap at `16 px`: `2 px`
- Minimum internal gap at `20 px`: `2.5 px`
- Minimum internal gap at `24 px`: `3 px`
- Never use more than:
  - one secondary cutout at `16 px`
  - two secondary cutouts at `20 px`
  - three interior details at `24 px`

### Optical alignment rules
- Center main masses on whole pixels
- Overshoot circles and diagonal endpoints by `0.5 px`
- Keep icon weight slightly bottom-heavy for readability on dark backgrounds
- Use diagonals only when the metaphor depends on direction
- Reserve dual-endpoint compositions for connection, correlation, and brand icons so those silhouettes stay distinctive

### Active / inactive treatment
- Default: `SecondaryTextColor`
- Hover: `PrimaryTextColor`
- Active nav: cyan linework plus one filled anchor node
- Selected nav should rely mainly on the pill background; the icon only needs a subtle accent fill, not a full inversion

### Disabled / error / success / warning variants
- Disabled: `DisabledTextColor`, no filled node, `45%` to `55%` effective contrast
- Success: `SuccessColor`, use filled terminal or status node
- Warning: `WarningColor`, use the same base glyph with a warning-state color where semantic meaning stays constant
- Error: `ErrorColor`, same rule as warning
- Do not create unique warning/error shapes for ordinary domain icons unless the shape itself is a state icon

### Color rules
- Default UI icons: `PrimaryTextColor` or `SecondaryTextColor`
- Download family: `DownloadColor`
- Upload family: `UploadColor`
- CPU family: `CpuColor`
- Memory family: `MemoryColor`
- Warning / error / success: semantic state colors only
- Brand icon: cyan plus orange is allowed; general UI icons should stay mostly single-color
- Avoid gradients for standard UI icons; gradients are reserved for the app brand only

### Sizing
- Dense table cells: `16 px`
- Toolbar and compact headers: `16` to `20 px`
- Nav rail and section headers: `20` to `24 px`
- Empty states: `32` to `48 px`
- App icon placeholders: `20`, `32`, `48`

### Naming convention
- `wb-brand-*`
- `wb-nav-*`
- `wb-action-*`
- `wb-metric-*`
- `wb-entity-*`
- `wb-status-*`
- `wb-adapter-*`

Examples:
- `wb-nav-overview`
- `wb-action-refresh`
- `wb-metric-download`
- `wb-entity-helper`
- `wb-status-warning`
- `wb-adapter-vpn`

### Export format
- Primary format: SVG
- ViewBox: `0 0 24 24` for standard icons
- Provide explicit small variants when needed:
  - `*-16.svg`
  - `*-20.svg`
- Raster fallbacks:
  - `16, 20, 24, 32` for UI fallback needs
  - `48, 64, 128, 256, 512` for brand/app icon distribution

### Accessibility rules
- Do not rely on color alone for meaning
- Keep silhouettes unique between:
  - `Connections` and `Correlations`
  - `Overview` and `Analytics`
  - `Settings` and `Tune`
  - `Refresh` and `Auto`
- Icon-only controls still need:
  - `AutomationProperties.Name`
  - minimum `32 x 32` hit target
- Maintain at least `3:1` contrast for non-text informative icons against their background

### Resolution-scaling rules from 720p to 4K
- `720p`: default to `16 px` dense icons and `20 px` primary icons; always use small variants for nav and table icons
- `1080p`: `16 px` dense icons, `20 px` headers, `24 px` nav icons
- `1440p`: `20 px` dense icons, `24 px` headers and nav, `32 px` empty states
- `4K`: `24 px` dense icons, `28` to `32 px` major UI icons, `48 px` empty states
- Use size-specific exports for raster fallbacks; do not upscale `16 px` PNGs
- If SVG is used at runtime, keep the same geometry but check visual weight at each target size and adjust stroke-expanded exports if needed

### Recommended minimum detail level
- At `16 px`, each icon should contain:
  - one dominant contour
  - one supporting contour or cutout
  - at most one filled node
- Remove inner ribs, connector slits, or secondary endpoints below `20 px` unless they are required for recognition

### Vector-first production requirements
- Expand strokes before delivery
- No masks, raster textures, or glow baked into SVG
- Avoid Gaussian blur and gradient meshes
- Prefer compound paths over nested groups when possible
- Keep origins, bounding boxes, and naming deterministic for developer handoff

## D. Proposed Icon Set

### Brand and navigation

| New icon name | Purpose | Visual metaphor | Shape description | State variants | Why it fits | Ambiguity risks |
|---|---|---|---|---|---|---|
| `wb-brand-wirebound` | App icon, tray icon, installer branding | One continuous cable linking an ingress node to an egress node | Rounded conductor forming an S-like loop between a cyan ring and an orange ring; simplified single-color 16 px tray variant | Full-color brand, monochrome tray, small simplified | Directly uses the best existing brand idea in the repo and makes the product memorable | If reused outside branding it can be mistaken for `Connections`; reserve it for brand only |
| `wb-nav-overview` | Overview route | Aggregated telemetry entering one hub | Three parallel traces converge into a rounded hub node | Active, inactive, 16 px simplified | Communicates “combined view” better than a generic bar chart | If the three traces become too even it can read as a list icon |
| `wb-nav-live` | Live Chart route | A live signal line moving through anchored sample points | One rising trace with three round sample nodes and a clean terminal | Active, inactive, 16 px simplified | Distinguishes time-series behavior from static summary | If the slope is too sharp it becomes a stock-finance icon |
| `wb-nav-apps` | Apps route | Process inventory / app grid | Four rounded app tiles connected by a tiny bus trace; one tile slightly open for emphasis | Active, inactive, placeholder fill | Reads as software/process inventory instead of cardboard packaging | If the bus trace is too subtle it can be mistaken for a dashboard icon |
| `wb-nav-connections` | Connections route | Linked endpoints | Two terminal rings bridged by a short conductor | Active, inactive | Strong, clean network metaphor tied to the brand language | Too similar to the brand icon if the bridge becomes too long or curved |
| `wb-nav-system` | System route | Device internals | Rounded chip outline with a single outbound trace | Active, inactive, 16 px simplified | Matches CPU/RAM focus without needing a desktop monitor silhouette | Could read as CPU-only if the chip is overly literal |
| `wb-nav-settings` | Settings route | Tunable controls | Three staggered sliders on a shared horizontal trace | Active, inactive | More product-specific and cleaner than a stock gear | At 16 px it can blur; needs a simplified small variant |

### Actions and controls

| New icon name | Purpose | Visual metaphor | Shape description | State variants | Why it fits | Ambiguity risks |
|---|---|---|---|---|---|---|
| `wb-action-tune` | Advanced adapters, columns, local control panels | Fine tuning | Compact vertical slider set with one emphasized node | Default, hover | Replaces miscellaneous gear usage with a utilitarian control icon | Must stay distinct from `wb-nav-settings` |
| `wb-action-refresh` | Refresh actions | Reload loop | Rounded loop arrow with a clipped terminal end | Default, busy | Feels mechanical and calm rather than emoji-like | Can be confused with auto-detect if the center stays empty |
| `wb-action-auto` | Auto adapter, auto-switch banner | Automatic routing | Open orbit arrow circling a center node | Default, active | The center node implies a selected target, which refresh does not have | If the orbit closes too much it reads as refresh |
| `wb-action-search` | Search fields, filtered empty state | Search lens + endpoint | Ring with a short trace handle | Default, empty-state large | Familiar enough to be instantly legible but still stylistically matched | None if kept simple |
| `wb-action-back` | Back to apps | Chevron trace | Thin leftward chevron with rounded ends | Default | Standard UI primitive; should not compete with branded icons | Avoid over-styling this |
| `wb-action-time-range` | Time range selectors | Window of time | Partial circular arc around a node with one short tick | Default, active | Cleaner than a stopwatch and better suited to compact controls | If too clock-like it competes with future scheduling meanings |

### Metrics, analytics, and state

| New icon name | Purpose | Visual metaphor | Shape description | State variants | Why it fits | Ambiguity risks |
|---|---|---|---|---|---|---|
| `wb-metric-download` | Download / received | Flow entering a tray | Downward conductor landing into an open receiver rail | Small, regular, active | Strong direction, simple silhouette, works at 16 px | None |
| `wb-metric-upload` | Upload / sent | Flow leaving a tray | Upward conductor lifting from a sender rail | Small, regular, active | Pairs with download cleanly and avoids stock arrows | None |
| `wb-metric-analytics` | Averages, session stats, dashboard customization | Ordered measurement | Three stepped metric lanes sharing a base trace | Default, active | Separates “analytics” from “overview” and “live chart” | If too bar-like it may resemble old dashboard iconography |
| `wb-metric-info` | Info banners | Neutral information marker | Filled node over a short centered stem, built from the same line weight as the set | Default | Works better than emoji while staying universally legible | Must not look like a lowercase `i` rendered as text |
| `wb-metric-insight` | Hints and insights | Spark of understanding | Central node with three short radiating traces | Default, emphasis | Less cute than a bulb and more aligned with signal/data language | If overdrawn it starts to look like a starburst |
| `wb-state-trend-up` | Rising trend | Micro rising trace | Short upward diagonal with a filled end node | 12 px, 16 px | Cleaner than text arrows and compatible with the wire metaphor | At tiny sizes, avoid secondary detail |
| `wb-state-trend-down` | Falling trend | Micro falling trace | Short downward diagonal with a filled end node | 12 px, 16 px | Same family as trend-up | Same as above |
| `wb-state-trend-stable` | Stable trend | Flat trace | Short horizontal lane with a centered node | 12 px, 16 px | Better matched than a plain `→` or `●` | If too short it looks like a minus sign |
| `wb-state-trend-idle` | No activity | Open idle node | Hollow node with short base tick | 12 px, 16 px | Gives “idle” its own quieter state | If the base tick is removed it becomes an empty radio circle |
| `wb-status-warning` | Warning banners and inline warnings | Caution beacon | Rounded triangle shell with a centered vertical trace | Default, filled badge | Uses a conventional warning shape where convention is beneficial | Keep it standard enough to be recognizable |
| `wb-status-error` | Error banners | Broken node | Rounded ring with a diagonal cut or broken crossbar | Default | Reads as error without leaning on text-only color | If too abstract, use a softer circle-x treatment |
| `wb-status-success` | Success states | Confirmed node | Filled node with a short check-like terminal | Default | Rare but useful for helper-connected or update-complete states | Keep check detail minimal at small sizes |
| `wb-status-update-dot` | Update available badge | Status primitive | Filled accent dot, optional subtle outer ring at large sizes | Dot only | No need to overdesign a nav badge | None |
| `wb-status-monitoring-dot` | Footer monitoring state | Status primitive | Solid dot that relies on color and motion, not shape | Active, idle, error | Existing treatment is already appropriate | None |

### Entities, hardware, and helper surfaces

| New icon name | Purpose | Visual metaphor | Shape description | State variants | Why it fits | Ambiguity risks |
|---|---|---|---|---|---|---|
| `wb-entity-app` | Generic app placeholder, app count | Desktop app tile | Rounded square tile with a subtle header cut or corner notch | Placeholder, small, large | Reads as software rather than shipping or storage | Must stay distinct from dashboard tiles |
| `wb-entity-cpu` | CPU section, top CPU app | Processor chip | Rounded chip outline with two inner core lanes | Small, regular, “hot” accent variant | Better match for technical tone than `🔥` or desktop monitor icons | Too much pin detail will fail at 16 px |
| `wb-entity-memory` | Memory section, top RAM app, memory alerts | Memory module | Rounded cartridge with three internal slots and a contact edge | Small, regular, alert variant | Gives CPU and memory their own clearly separated hardware family | If drawn too short it can resemble a battery |
| `wb-entity-thermal` | CPU temperature | Thermal sensor | Bulb node with a short stem and top cap | Small, regular | Clear and compact for metadata rows | Standard thermometer is okay; do not over-stylize |
| `wb-entity-cores` | CPU core count | Core matrix | Four tiny rounded cells in a chip frame | Small only | More semantically precise than `🔢` | At very small sizes it can become noise; use a simplified 2x2 grid |
| `wb-entity-peak` | Peak memory, peak metrics | Cresting trace | Rising trace that crests over a short baseline and ends in a node | Default | Communicates peak without reusing the full live-chart icon | If too extended it resembles the nav live icon |
| `wb-entity-correlation` | Correlations tab and relation cards | Shared center | Two traces crossing at a filled center node with matched endpoints | Small, regular | Expresses relation between two live systems without pretending to be a literal network connection | Can be confused with merge/intersection if endpoint balance is poor |
| `wb-entity-network-node` | Remote hosts, network destinations, generic network | Remote endpoint | Concentric ring with short branching stubs or orbit notches | Small, regular, large empty-state variant | Better than a globe for concrete remote endpoints and destinations | If too circular it turns back into a globe substitute |
| `wb-entity-helper` | Elevated Helper section and CTAs | Trusted bridge | Rounded shield shell containing a small terminal node and trace | Default, connected, disconnected | Keeps the trust/security meaning but makes it feel native to WireBound | Shield metaphors always risk “antivirus” associations |
| `wb-entity-preview` | Byte tracking preview / limited measurements | Probe on conductor | Small probe or sampler touching a main trace and node | Default, limited | Better fit for “preview/estimated” than a microscope | If the probe looks medical or lab-like it may feel off-topic |
| `wb-entity-disconnected` | No active connections empty state | Unplugged path | Separated plug tip and socket ring with a visible gap | Default, large empty-state | Strong empty-state metaphor and shares geometry with wired adapters | Must not be confused with Ethernet when the gap is hidden |

### Adapter family

| New icon name | Purpose | Visual metaphor | Shape description | State variants | Why it fits | Ambiguity risks |
|---|---|---|---|---|---|---|
| `wb-adapter-wifi` | Wi-Fi adapters | Wireless signal from a cable base | Two or three rounded arcs rising from a short base trace | `wifi-1` to `wifi-4` signal variants | Strong semantics, easy to read in combo boxes and inline chips | Avoid too many arcs at 16 px |
| `wb-adapter-ethernet` | Ethernet adapters | Wired terminal | Compact RJ-style terminal with a short cable tail | Default, disconnected relation via `wb-entity-disconnected` | Stable metaphor and consistent with brand cable forms | Keep contact details minimal |
| `wb-adapter-vpn` | VPN adapters | Secured tunnel | Conductor passing through a guard ring | Default, active | Communicates protected routing without using a lock emoji | Could be confused with helper/security if the ring becomes shield-like |
| `wb-adapter-usb` | USB tethering | Device tether | Simplified forked cable head with a short stem | Default | More precise than a phone emoji for the network transport | If too literal it becomes a generic USB icon from any OS pack |
| `wb-adapter-bt-tether` | Bluetooth tethering | Short-range paired link | Two small nodes linked by an angled trace with one tiny wireless notch | Default | Avoids reusing the standard Bluetooth rune while still implying device-to-device link | Too abstract if the wireless hint disappears |
| `wb-adapter-tunnel` | Tunnel adapters | Routed path through a passage | Conductor line passing through a rounded arch | Default | Clear enough for technical users and consistent with the name | Could read as VPN if the arch resembles a shield |
| `wb-adapter-loopback` | Loopback adapters | Return-to-self | Trace looping back into its origin node | Default | Elegant and semantically strong | Can look too similar to refresh; keep the origin node fixed and no arrowhead |
| `wb-adapter-virtual` | Virtual adapters | Layered ghost adapter | Standard adapter silhouette with a faint offset duplicate behind it | Default | Signals “software-defined” without using a desktop monitor icon | At tiny sizes the offset duplicate can disappear |
| `wb-adapter-generic` | Unknown or other adapters | Plain network endpoint | Single node with short horizontal trace | Default | Neutral fallback that still belongs to the family | Must not outrank specific adapter icons visually |

## E. Generation Briefs

### Reusable master style prompt
Create a vector-first desktop UI icon set for a privacy-focused network and system monitoring app named WireBound. Use a custom `Wire Trace` style: 24x24 grid, 20x20 live area, 2 px rounded stroke, rounded joins, one optional filled node per icon, strong negative space, dark-UI readability, no text, no stock-icon-pack look, no photorealism, no gradients except the brand icon, and no tiny decorative details. Base the geometry on cable traces, terminal nodes, signal lanes, rounded chips, and compact hardware silhouettes. Icons must remain legible at 16 px, 20 px, 24 px, and 32 px; provide simplified small variants where needed. Use monochrome outlines by default, with semantic color variants for download, upload, CPU, memory, warning, error, and success. Export clean SVG paths with no masks, no raster effects, and deterministic naming.

### Individual icon prompts

| Icon | Prompt |
|---|---|
| `wb-brand-wirebound` | Using the master style, draw the WireBound brand icon as one continuous cable connecting a cyan ingress ring to an orange egress ring, with a compact rounded loop and a simplified monochrome 16 px variant. |
| `wb-nav-overview` | Draw three parallel data traces converging into one rounded hub node; avoid bar-chart and clipboard semantics; silhouette must read clearly at 16 px. |
| `wb-nav-live` | Draw a live signal trace that rises through three anchored sample nodes and ends cleanly on the right; avoid looking like a stock-market icon. |
| `wb-nav-apps` | Draw four rounded app tiles connected by a short bus trace, suggesting a process inventory rather than packages or folders. |
| `wb-nav-connections` | Draw two terminal rings bridged by a short conductor, compact and centered, clearly distinct from the brand icon. |
| `wb-nav-system` | Draw a rounded chip outline with one outbound trace, communicating system internals rather than a desktop monitor shell. |
| `wb-nav-settings` | Draw three staggered slider tracks on a shared horizontal bus; no gears; preserve clarity at 16 px. |
| `wb-action-tune` | Draw a compact vertical tuning icon with two or three slider stems and one emphasized node, lighter and more utilitarian than the settings icon. |
| `wb-action-refresh` | Draw a rounded reload loop with a clipped arrow terminal; keep the center empty so it differs from auto-detect. |
| `wb-action-auto` | Draw an open orbit arrow circling a center node, conveying automatic target selection or automatic switching. |
| `wb-action-search` | Draw a magnifier integrated into the wire-trace style using a rounded ring and a short handle trace. |
| `wb-action-back` | Draw a minimal left chevron with rounded ends that matches the stroke weight of the icon system but remains a standard UI primitive. |
| `wb-action-time-range` | Draw a time-window icon as a partial arc around a node with one short tick mark; avoid stopwatch character. |
| `wb-metric-download` | Draw a downward conductor settling into an open receiver rail, optimized for 16 px and 24 px. |
| `wb-metric-upload` | Draw an upward conductor lifting from a sender rail, clearly paired with the download icon. |
| `wb-metric-analytics` | Draw three stepped measurement lanes on a shared base trace, distinct from both overview and live-chart navigation icons. |
| `wb-metric-info` | Draw a neutral information marker as a filled node over a short centered stem, using icon geometry rather than typography. |
| `wb-metric-insight` | Draw a compact insight spark: one central node with three short radiating traces, technical rather than whimsical. |
| `wb-state-trend-up` | Draw a tiny rising trend glyph as a short upward trace with a filled end node; no extra detail beyond what survives at 12 px. |
| `wb-state-trend-down` | Draw a tiny falling trend glyph as a short downward trace with a filled end node. |
| `wb-state-trend-stable` | Draw a tiny stable trend glyph as a flat short lane with one centered node, distinct from a minus sign. |
| `wb-state-trend-idle` | Draw a tiny idle glyph as a hollow node with a short grounding tick, visually quieter than stable. |
| `wb-status-warning` | Draw a rounded warning beacon with a centered vertical trace; keep it conventional enough for fast recognition. |
| `wb-status-error` | Draw a rounded error icon based on a broken ring or softened circle-x, aligned to the same stroke system. |
| `wb-status-success` | Draw a compact success icon based on a confirmed node with a minimal check-like terminal, clean at 16 px. |
| `wb-entity-app` | Draw a generic app tile with a subtle header cut or corner notch; no package-box or folder semantics. |
| `wb-entity-cpu` | Draw a rounded processor chip with two inner core lanes; small variant removes unnecessary pin detail. |
| `wb-entity-memory` | Draw a memory module with three internal slots and a clean contact edge, distinct from a battery icon. |
| `wb-entity-thermal` | Draw a compact thermometer or thermal sensor icon matched to the wire-trace stroke style. |
| `wb-entity-cores` | Draw a 2x2 core matrix inside a chip frame, optimized for tiny metadata usage. |
| `wb-entity-peak` | Draw a cresting line trace that peaks over a short baseline and ends in a node, more compact than the live-chart icon. |
| `wb-entity-correlation` | Draw two balanced traces crossing at a shared center node to imply relationship, not physical cable connection. |
| `wb-entity-network-node` | Draw a remote-endpoint icon as a concentric node with subtle branching stubs or orbit cuts, avoiding a generic globe. |
| `wb-entity-helper` | Draw a trusted-helper icon as a rounded shield shell containing a terminal node and short trace, not a stock antivirus shield. |
| `wb-entity-preview` | Draw a preview/probe icon as a small sampler touching a main conductor, clearly meaning estimated or inspected data. |
| `wb-entity-disconnected` | Draw a disconnected network state as a separated plug tip and socket ring with a visible intentional gap. |
| `wb-adapter-wifi` | Draw Wi-Fi as two or three clean arcs rising from a cable base; provide 1 to 4 signal variants by arc count or filled-state treatment. |
| `wb-adapter-ethernet` | Draw a compact wired terminal with a short cable tail, simplified enough to survive in combo boxes. |
| `wb-adapter-vpn` | Draw a protected route as a conductor line passing through a guard ring, clearly distinct from the helper icon. |
| `wb-adapter-usb` | Draw a tethering icon based on a simplified USB cable head and stem, avoiding stock-symbol clutter. |
| `wb-adapter-bt-tether` | Draw a short-range tether icon using paired nodes and a small angled trace with a subtle wireless hint, not the default Bluetooth rune. |
| `wb-adapter-tunnel` | Draw a routed path passing through a rounded arch to represent tunnel adapters. |
| `wb-adapter-loopback` | Draw a conductor that loops back into its own origin node; do not add an arrowhead. |
| `wb-adapter-virtual` | Draw a virtual-adapter icon as a main adapter silhouette with a faint offset duplicate behind it. |
| `wb-adapter-generic` | Draw a neutral network adapter fallback as a single endpoint node with a short trace, intentionally plain. |

## F. Implementation Handoff

### Recommended asset naming
- `wb-brand-wirebound.svg`
- `wb-brand-wirebound-16.svg`
- `wb-nav-overview.svg`
- `wb-nav-live.svg`
- `wb-nav-apps.svg`
- `wb-nav-connections.svg`
- `wb-nav-system.svg`
- `wb-nav-settings.svg`
- `wb-action-refresh.svg`
- `wb-metric-download.svg`
- `wb-status-warning.svg`
- `wb-adapter-wifi-4.svg`

### Recommended folder structure

```text
src/WireBound.Avalonia/Assets/Icons/
  brand/
  nav/
  action/
  metric/
  entity/
  status/
  adapter/
  small/
```

Example:

```text
src/WireBound.Avalonia/Assets/Icons/
  brand/wb-brand-wirebound.svg
  brand/wb-brand-wirebound-16.svg
  nav/wb-nav-overview.svg
  nav/wb-nav-live.svg
  nav/wb-nav-apps.svg
  nav/wb-nav-connections.svg
  nav/wb-nav-system.svg
  nav/wb-nav-settings.svg
  action/wb-action-refresh.svg
  metric/wb-metric-download.svg
  metric/wb-metric-upload.svg
  entity/wb-entity-app.svg
  status/wb-status-warning.svg
  adapter/wb-adapter-wifi-4.svg
  small/wb-entity-correlation-16.svg
```

### Export checklist
- Standard SVG viewBox is `0 0 24 24`
- Expand strokes before handoff
- Snap to pixel grid after expansion
- Provide `16 px` small variants for:
  - `wb-nav-live`
  - `wb-nav-settings`
  - `wb-entity-correlation`
  - `wb-entity-helper`
  - `wb-adapter-vpn`
  - `wb-adapter-virtual`
- Provide semantic-color previews, but ship base monochrome SVGs when icons are recolored in UI
- Verify icon center, bounding box, and optical weight on dark surfaces
- Ensure all icon filenames are stable and ASCII-only

### Developer notes
- Replace string emoji usage with named icon assets or a small `IconKey` enum
- Keep arrows, dots, and tiny sort glyphs as UI primitives if that reduces implementation overhead
- Use the same icon asset for repeated meanings across views:
  - one download icon
  - one upload icon
  - one app placeholder icon
  - one helper icon
- Keep status dots as primitives; do not replace them with pictograms
- Consider a thin icon wrapper so nav, toolbar, metric, and empty-state sizes can be standardized centrally

### Raster fallback guidance
- Only use raster fallbacks for:
  - tray icon
  - app/installer branding
  - any platform surface that cannot render SVG reliably
- Export raster fallbacks at exact target sizes; never scale the `16 px` raster tray icon up for general UI use
- Provide transparent PNG fallbacks for `16, 20, 24, 32`

### Icons needing clarification before final production
- `History` still appears in old design docs, but the live app currently exposes six routes, not seven
- Settings contains an `Insights Page` section even though there is no separate insights route in the current nav
- Decide whether `System` remains a long-term primary nav destination or eventually collapses into `Overview`
- Confirm whether Bluetooth tether, USB tether, tunnel, and virtual-adapter icons all need distinct first-release assets or whether some can ship as phase-two additions
- Confirm whether the top-destinations area in `Apps` is a permanent feature surface worth a dedicated remote-endpoint empty-state icon

## G. Quality Review

### Strengths
- The proposed system is specific to WireBound instead of feeling borrowed from a generic icon pack
- It converts the repo’s existing cable/node branding experiments into a usable full-product icon language
- It removes OS-dependent emoji rendering differences
- It gives the nav clearer semantic separation, especially between `Overview`, `Live Chart`, `Connections`, and `System`
- It supports dense desktop UI usage from tiny table cells to large empty states

### Weak spots
- `Correlation` is the hardest concept in the set and will need careful optical tuning
- `Preview / limited measurement` is semantically niche and may need product copy to help it
- `Helper` can drift too far toward security-software aesthetics if the shield becomes dominant
- `Overview` and `Analytics` must stay clearly different or the old `📊` overload will return in a new form

### Icons to test with users
- `wb-nav-overview` versus `wb-metric-analytics`
- `wb-nav-connections` versus `wb-entity-correlation`
- `wb-entity-helper` versus `wb-status-warning`
- `wb-entity-app` versus any dashboard/grid-like icon
- `wb-entity-preview` in the byte-tracking-preview banner

### Final recommendations
- Implement in phases:
  1. brand, nav, download/upload, search, refresh, warning/error, app placeholder
  2. CPU, memory, helper, correlation, analytics
  3. full adapter family and preview/limited-state icons
- Let code drive the first replacement pass, not the older docs
- Normalize icon semantics before generating assets:
  - one icon per meaning
  - one meaning per icon
- After visual approval, replace string-based icon properties with stable asset keys so future UI work does not reintroduce emoji drift
