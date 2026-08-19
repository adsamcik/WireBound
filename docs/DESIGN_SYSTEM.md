# WireBound Design System v3.0

## Signal & Flow

WireBound is a technical monitoring tool that should feel calm under pressure.
Its interface uses restrained, desktop-native structure for routine work and
expressive color only where it improves interpretation.

The system combines two complementary influences:

- Material 3 Expressive contributes purposeful color, contrasting shape,
  generous selected states, and containment that directs attention.
- Fluent 2 contributes compact desktop density, neutral command surfaces,
  clear interaction states, and restrained elevation.

WireBound does not reproduce either system. The resulting language is called
**Signal & Flow**: quiet infrastructure with vivid, meaningful signals.

## Principles

### 1. Data is the expression

Resource colors belong to measurements, selected state, and status. Labels,
panel outlines, and ordinary navigation remain neutral. A screen showing all
four resource colors at once should still feel calm.

### 2. Tonal depth before borders

Hierarchy comes from a small ramp of neutral surfaces. Borders are used only
when adjacent surfaces would otherwise merge, for keyboard focus, or for a
selected resource. Avoid drawing a bright rectangle around every region.

### 3. Shape communicates role

- 12–14 px: compact inputs and row objects
- 18–20 px: toolbars, resource controls, and secondary containers
- 24–28 px: primary dashboard panels and floating surfaces
- pill: status, mutually exclusive choices, and compact primary actions

Varying shape establishes hierarchy. Do not give every object the same radius.

### 4. Density follows the task

Dashboards may breathe around interpretation. Tables and process lists are
dense because comparison is their primary job. A decorative surface must never
reduce the useful list viewport without adding information or action.

### 5. Motion confirms; it does not perform

Use short state transitions for hover, selection, filtering, and navigation.
Do not automatically switch resource focus or animate continuously around live
data. Monitoring should remain visually stable while values change.

## Color system

### Neutral surfaces

| Token | Value | Role |
|---|---:|---|
| Background | `#0B1017` | Base canvas |
| Rail | `#0E151E` | Header and app chrome |
| Surface | `#121A24` | Standard contained region |
| Card | `#141E29` | Primary content panel |
| Surface variant | `#161F2A` | Toolbars, chart wells, grouped controls |
| Elevated | `#1B2633` | Floating toolbar, hover, transient emphasis |
| Border | `#263342` | Focus and necessary separation |
| Divider | `#202B37` | Low-contrast row separation |

The app backdrop uses a very subtle navy gradient. Content surfaces stay mostly
opaque for performance, legibility, and cross-platform consistency. Acrylic is
reserved for transient UI where the platform can render it reliably.

### Text

| Token | Value | Role |
|---|---:|---|
| Primary | `#F4F7FA` | Titles, values, names |
| Secondary | `#B2BDCA` | Descriptions and ordinary labels |
| Muted | `#7C8998` | Timestamps, metadata, inactive state |

### Resource signals

| Resource | Signal | Tint use |
|---|---:|---|
| Network / download | `#66D7E5` | Selection, chart line, throughput |
| Upload | `#F0A384` | Upload values and secondary network series |
| CPU | `#83A9F9` | CPU selection and chart line |
| Memory | `#F08BBE` | Memory selection and chart line |
| Disk | `#F2C66D` | Disk selection and chart line |
| Success | `#68D6AE` | Healthy live state |
| Error | `#FF8A9A` | Failure and destructive warning |

Resource colors have similar perceived brightness so focus does not jump simply
because one resource is selected. Each has a translucent tint token for filled
selection and icon containers.

### Color rules

- Prefer primary text for resource names; use resource color on the icon,
  measured value, or selected container.
- Color is never the only indication of state. Pair it with text, shape, an
  icon, or a directional marker.
- One primary action per region. Equal-priority actions use neutral fills.
- Avoid neon glows. A selected resource may use one colored stroke plus a tonal
  fill; unselected cards do not receive colored outlines.

## Typography

WireBound uses Inter through Avalonia for consistent cross-platform metrics.
Segoe UI Variable may be used by native Windows surfaces.

| Style | Size | Weight | Use |
|---|---:|---|---|
| Page title | 30 | Semibold | Rare full-page headings |
| Panel title | 19–22 | Semibold | Primary dashboard regions |
| Section title | 18 | Semibold | Secondary sections |
| Body | 14 | Regular | General content |
| Compact body | 13 | Regular/Semibold | Tables and toolbars |
| Caption | 12 | Regular | Metadata |
| Eyebrow | 10–11 | Semibold | Short contextual labels |

Technical values use tabular alignment where possible. Uppercase is limited to
short eyebrows and table headings; never uppercase paragraphs or actions.

## Components

### Header navigation

Primary destinations form one contained group in the header. The selected item
uses a filled tonal state; unselected items remain borderless. Monitoring status
is a separate pill and settings is a circular icon action.

### Resource selector

The four resources are equal sibling summary cards ordered CPU, Memory, Disk,
then Network. Each card presents one
primary current value, one supporting measurement, and a lightweight recent
sparkline. The whole card is the target; only the selected resource receives a
resource-colored border and stronger fill.

Resource focus changes only through explicit user action. It never rotates
automatically in response to load.

### Primary dashboard panels

Activity and contributors are sibling 26 px panels. They use low elevation and
a subtle edge, not a blue outline. Charts sit in a quieter nested chart well.
Contributor entries are individual tonal row objects rather than divider-only
spreadsheet rows.

### Contextual view toolbar

Time range, signal, and process scope sit immediately above the chart they
affect. Infrequent contextual choices, such as network adapter, open in a
popover so they do not displace monitoring content. View controls never reserve
a separate footer row.

### Process workspace

Search, scope, refresh, and sort belong to the process table surface. Sortable
headers use the entire 40 px header cell as their target with persistent selected
state and a separate direction glyph. Rows are 52 px and virtualized.

## Charts

- Use a 2–2.25 px resource-colored line.
- Use a low-opacity fill so spikes remain readable without coloring the panel.
- Grid lines are quieter than labels; labels are quieter than the series.
- Hide a legend when the chart has only one series and the panel title already
  names it.
- Avoid large vertical axis titles in live dashboard charts. Units belong in
  tick labels or a compact contextual label.
- Never allow an unbounded contributor list or chart series to expand the
  dashboard indefinitely.

## Interaction and accessibility

- Minimum compact desktop target: 40 px. Prefer 44 px for primary controls.
- Every icon-only action has a tooltip and `AutomationProperties.Name`.
- Selected state combines fill or outline with an accessible control state.
- Text contrast targets WCAG AA; icons target at least 3:1 against their surface.
- Keyboard focus must remain visible independently of resource color.
- Respect reduced-motion and performance settings by removing decorative
  transitions, shadows, and blur before removing information.

## Responsive behavior

- Wide: activity and contributors are side by side at roughly 2.2:1.
- Below 1240 px: contributors move below the chart so controls and data keep
  useful width.
- Process and connection tables preserve a finite vertical viewport and share
  one horizontal scroll position between header and rows.
- Contextual controls stay with the chart and wrap into the wider stacked layout
  before content or data becomes illegible.

## Implementation sources

- Colors and brushes: `src/WireBound.Avalonia/Styles/Colors.axaml`
- Shared component styles: `src/WireBound.Avalonia/Styles/Styles.axaml`
- Chart colors: `src/WireBound.Core/Helpers/ChartColors.cs`
- Header navigation: `src/WireBound.Avalonia/Views/MainWindow.axaml`
- Contextual view controls: `src/WireBound.Avalonia/Views/OverviewView.axaml`
- Unified dashboard: `src/WireBound.Avalonia/Views/OverviewView.axaml`

New components should use these tokens instead of embedding new hex values.
