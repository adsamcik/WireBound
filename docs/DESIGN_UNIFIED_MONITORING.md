# WireBound Unified Monitoring Design v1.0

## Executive Summary

This document outlines the redesign of WireBound from a network-focused monitoring tool to a **unified system monitoring experience** with network as the primary focus. The design emphasizes exceptional UX/UI while introducing CPU, Memory, and future GPU monitoring capabilities.

---

## Design Philosophy

### Core Principles

1. **Network-First, System-Aware**: Network monitoring remains the hero, but system context enhances the story
2. **Progressive Disclosure**: Show essential data first, reveal complexity on demand
3. **Contextual Correlation**: Help users understand how system resources relate to network activity
4. **Visual Hierarchy**: Use size, color, and position to establish importance
5. **Performance-Conscious**: Monitoring tools shouldn't burden the system they monitor

### The "Flow" Metaphor

Extending the "Fluid Data" design vision:
- **Network = The River**: Primary data flow, largest visual presence
- **CPU = The Engine**: Processing power driving the flow
- **Memory = The Reservoir**: Capacity and availability
- **GPU = The Accelerator**: (Future) Boosting specific workloads

---

## Information Architecture

### Current Navigation (6 routes)
```
Dashboard → Charts → History → Applications → Connections → System → Settings
```

### Proposed Navigation (5 routes + Settings)
```
┌─────────────────────────────────────────────────────────────────┐
│                        WireBound                                │
├─────────────────────────────────────────────────────────────────┤
│  📊 Overview      │ Unified real-time dashboard                 │
│  📈 Live Charts   │ Detailed multi-metric charting              │
│  📱 Applications  │ Per-app network usage (unchanged)           │
│  🔗 Connections   │ Active connections (unchanged)              │
│  📅 Insights      │ Unified history + statistics + trends       │
│  ⚙️ Settings      │ Configuration (unchanged)                   │
└─────────────────────────────────────────────────────────────────┘
```

### Key Changes

| Old Route | New Route | Rationale |
|-----------|-----------|-----------|
| Dashboard | **Overview** | Unified real-time view with network + system metrics |
| Charts | **Live Charts** | Multi-metric charting with overlay capabilities |
| History | → Insights | Merged into comprehensive insights page |
| System | → Overview | Integrated into main dashboard as secondary metrics |

---

## Page Designs

### 1. Overview Page (Unified Dashboard)

The hero page combining network monitoring with system awareness.

#### Layout: Adaptive Bento Grid

```
┌──────────────────────────────────────────────────────────────────────────┐
│ Header: "Overview" + Adapter Selector + Quick System Strip              │
├────────────────────────────────────┬─────────────────────────────────────┤
│                                    │                                     │
│  ╔══════════════════════════════╗  │  ╔═══════════════════════════════╗  │
│  ║  DOWNLOAD SPEED (Hero Card)  ║  │  ║   UPLOAD SPEED (Hero Card)    ║  │
│  ║                              ║  │  ║                               ║  │
│  ║       ↓ 124.5 MB/s           ║  │  ║        ↑ 15.2 MB/s            ║  │
│  ║      Today: 2.4 GB           ║  │  ║       Today: 890 MB           ║  │
│  ╚══════════════════════════════╝  │  ╚═══════════════════════════════╝  │
│                                    │                                     │
├────────────────────────────────────┴─────────────────────────────────────┤
│                                                                          │
│  ╔════════════════════════════════════════════════════════════════════╗  │
│  ║                    LIVE NETWORK CHART                              ║  │
│  ║  [1m] [5m] [15m] [1h]                            Toggle Layers ▼   ║  │
│  ║  ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~  ║  │
│  ║  Download/Upload real-time with optional CPU/Memory overlay        ║  │
│  ╚════════════════════════════════════════════════════════════════════╝  │
│                                                                          │
├────────────────────────────────────┬─────────────────────────────────────┤
│                                    │                                     │
│  ╔══════════════════════════════╗  │  ╔═══════════════════════════════╗  │
│  ║   SESSION STATS (Card)       ║  │  ║   SYSTEM HEALTH (Card)        ║  │
│  ║                              ║  │  ║                               ║  │
│  ║   Duration: 2h 34m           ║  │  ║   CPU   ████████░░  78%       ║  │
│  ║   Downloaded: 8.2 GB         ║  │  ║   RAM   ██████░░░░  62%       ║  │
│  ║   Uploaded: 1.4 GB           ║  │  ║   GPU   ████░░░░░░  38%       ║  │
│  ║   Avg Speed: 12.4 MB/s       ║  │  ║                               ║  │
│  ╚══════════════════════════════╝  │  ╚═══════════════════════════════╝  │
│                                    │                                     │
└────────────────────────────────────┴─────────────────────────────────────┘
```

#### Header Quick System Strip

A compact, always-visible strip showing system health at a glance:

```
┌──────────────────────────────────────────────────────────────────────────┐
│ Overview                          [Adapter ▼]  CPU:45% RAM:62% [GPU:38%]│
└──────────────────────────────────────────────────────────────────────────┘
```

- Circular mini-gauges or compact bars
- Click expands to full System Health card
- Color-coded: Green (<70%), Yellow (70-85%), Red (>85%)

#### System Health Card Details

When expanded or viewed in card form:

```
╔═══════════════════════════════════════════════════════════════════════╗
║ SYSTEM HEALTH                                              [Expand ↗] ║
╠═══════════════════════════════════════════════════════════════════════╣
║                                                                       ║
║  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐                   ║
║  │     CPU     │  │   Memory    │  │     GPU     │                   ║
║  │    ╭───╮    │  │    ╭───╮    │  │    ╭───╮    │                   ║
║  │   │78%│    │  │   │62%│    │  │   │38%│    │                   ║
║  │    ╰───╯    │  │    ╰───╯    │  │    ╰───╯    │                   ║
║  │ AMD Ryzen 9 │  │ 20/32 GB    │  │ RTX 4080    │                   ║
║  │  5950X      │  │ Available:  │  │ VRAM: 6/16  │                   ║
║  │  16 cores   │  │   12 GB     │  │    GB       │                   ║
║  │  4.2 GHz    │  │             │  │   45°C      │                   ║
║  └─────────────┘  └─────────────┘  └─────────────┘                   ║
║                                                                       ║
║  [View Detailed System Monitor →]                                     ║
╚═══════════════════════════════════════════════════════════════════════╝
```

---

### 2. Live Charts Page (Multi-Metric Charting)

Advanced charting with layer toggles and correlation views.

#### Features

1. **Primary Chart Area**: Full-width, interactive chart
2. **Metric Toggles**: Show/hide different metrics as overlays
3. **Time Range Selection**: 1m, 5m, 15m, 1h, 6h, 24h
4. **Dual Y-Axis**: Speed (left) vs Percentage (right) for CPU/Memory overlay
5. **Zoom & Pan**: Interactive navigation through data

#### Layout

```
┌──────────────────────────────────────────────────────────────────────────┐
│ Live Charts                                                              │
├──────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  Time Range: [1m] [5m] [15m] [1h] [6h] [24h]                            │
│                                                                          │
│  Layers: [✓ Download] [✓ Upload] [○ CPU] [○ Memory] [○ GPU]             │
│                                                                          │
│  ╔════════════════════════════════════════════════════════════════════╗  │
│  ║ MB/s                                                          %    ║  │
│  ║  150 ┤                                                      ┤ 100  ║  │
│  ║      │     ╭─╮                                              │      ║  │
│  ║  100 ┤    ╭╯ ╰╮   ╭──╮                                     ┤ 75   ║  │
│  ║      │   ╭╯   ╰╮ ╭╯  ╰╮                                    │      ║  │
│  ║   50 ┤──╯      ╰╯     ╰──╮                                 ┤ 50   ║  │
│  ║      │                    ╰──────                          │      ║  │
│  ║    0 ┼────────────────────────────────────────────────────┼ 0    ║  │
│  ║      └────────────────────────────────────────────────────┘      ║  │
│  ║        12:00    12:05    12:10    12:15    12:20    12:25        ║  │
│  ╚════════════════════════════════════════════════════════════════════╝  │
│                                                                          │
│  Chart Legend:                                                           │
│  ━━━ Download  ━━━ Upload  ┄┄┄ CPU  ┄┄┄ Memory                          │
│                                                                          │
├──────────────────────────────────────────────────────────────────────────┤
│ INSIGHTS STRIP                                                           │
│ Peak Download: 145 MB/s @ 12:07  │  Avg Upload: 8.2 MB/s  │  Corr: 0.72 │
└──────────────────────────────────────────────────────────────────────────┘
```

---

### 3. Insights Page (Unified History + Statistics)

Consolidates historical data and provides actionable insights.

#### Tabs Structure

```
┌──────────────────────────────────────────────────────────────────────────┐
│ Insights                                                                 │
├──────────────────────────────────────────────────────────────────────────┤
│ [Network Usage] [System Trends] [Correlations] [Export]                  │
└──────────────────────────────────────────────────────────────────────────┘
```

#### Tab 1: Network Usage (Current History View Enhanced)

```
┌──────────────────────────────────────────────────────────────────────────┐
│ Network Usage                                                            │
├──────────────────────────────────────────────────────────────────────────┤
│ Period: [Today] [This Week] [This Month] [Custom Range]                  │
│                                                                          │
│  SUMMARY CARDS                                                           │
│  ┌────────────────┬────────────────┬────────────────┬────────────────┐   │
│  │ Total Download │ Total Upload   │ Peak Download  │ Peak Upload    │   │
│  │   145.8 GB     │    28.4 GB     │   245 MB/s     │   89 MB/s      │   │
│  │ ▲ 12% vs last  │ ▼ 8% vs last   │ Jan 15, 2:34pm │ Jan 18, 9:12am │   │
│  └────────────────┴────────────────┴────────────────┴────────────────┘   │
│                                                                          │
│  DAILY BREAKDOWN                                                         │
│  ╔════════════════════════════════════════════════════════════════════╗  │
│  ║  Bar chart showing daily download/upload                           ║  │
│  ╚════════════════════════════════════════════════════════════════════╝  │
│                                                                          │
│  HOURLY PATTERN                                                          │
│  ╔════════════════════════════════════════════════════════════════════╗  │
│  ║  Heatmap showing usage patterns by hour/day                        ║  │
│  ╚════════════════════════════════════════════════════════════════════╝  │
└──────────────────────────────────────────────────────────────────────────┘
```

#### Tab 2: System Trends

```
┌──────────────────────────────────────────────────────────────────────────┐
│ System Trends                                                            │
├──────────────────────────────────────────────────────────────────────────┤
│ Period: [Today] [This Week] [This Month]                                 │
│                                                                          │
│  RESOURCE SUMMARY                                                        │
│  ┌────────────────┬────────────────┬────────────────┐                    │
│  │ Avg CPU Usage  │ Avg Memory     │ Peak Memory    │                    │
│  │     34%        │     58%        │    92%         │                    │
│  │ Normal range   │ Healthy        │ Jan 17, 4:15pm │                    │
│  └────────────────┴────────────────┴────────────────┘                    │
│                                                                          │
│  HISTORICAL CHART                                                        │
│  ╔════════════════════════════════════════════════════════════════════╗  │
│  ║  Line chart: CPU, Memory over selected period                      ║  │
│  ║  Aggregate view: Hourly averages for week/month                    ║  │
│  ╚════════════════════════════════════════════════════════════════════╝  │
└──────────────────────────────────────────────────────────────────────────┘
```

#### Tab 3: Correlations

```
┌──────────────────────────────────────────────────────────────────────────┐
│ Correlations                                                             │
├──────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  "How does network activity affect your system?"                         │
│                                                                          │
│  CORRELATION MATRIX                                                      │
│  ┌─────────┬──────────┬────────┬────────┬────────┐                       │
│  │         │ Download │ Upload │ CPU    │ Memory │                       │
│  ├─────────┼──────────┼────────┼────────┼────────┤                       │
│  │Download │    -     │  0.45  │  0.72  │  0.38  │                       │
│  │Upload   │   0.45   │   -    │  0.61  │  0.29  │                       │
│  │CPU      │   0.72   │  0.61  │   -    │  0.55  │                       │
│  │Memory   │   0.38   │  0.29  │  0.55  │   -    │                       │
│  └─────────┴──────────┴────────┴────────┴────────┘                       │
│                                                                          │
│  INSIGHTS                                                                │
│  • High network activity correlates with 72% higher CPU usage            │
│  • Memory impact from downloads is minimal (correlation: 0.38)           │
│  • Peak network times: 9-11 AM, 2-4 PM                                   │
│                                                                          │
│  OVERLAY CHART                                                           │
│  ╔════════════════════════════════════════════════════════════════════╗  │
│  ║  Dual-axis chart showing network + system metrics together         ║  │
│  ╚════════════════════════════════════════════════════════════════════╝  │
└──────────────────────────────────────────────────────────────────────────┘
```

#### Tab 4: Export

- Export data as CSV, JSON
- Generate PDF reports
- Schedule automated exports

---

## Visual Design Specifications

### Color Palette Extensions

Building on the existing "Deep Ocean" theme:

| Metric | Primary Color | Glow/Light | Dim | Background Tint |
|--------|--------------|------------|-----|-----------------|
| Download | `#00E5FF` | `#00D4FF` | `#0099AA` | `#1000E5FF` |
| Upload | `#FF6B35` | `#FF8C5A` | `#CC5529` | `#10FF6B35` |
| **CPU** | `#3B82F6` | `#60A5FA` | `#2563EB` | `#103B82F6` |
| **Memory** | `#A855F7` | `#C084FC` | `#9333EA` | `#10A855F7` |
| **GPU** | `#10B981` | `#34D399` | `#059669` | `#1010B981` |

### New Color Resources (Colors.axaml additions)

```xml
<!-- CPU COLORS -->
<Color x:Key="CpuColor">#3B82F6</Color>
<Color x:Key="CpuColorLight">#60A5FA</Color>
<Color x:Key="CpuColorDark">#2563EB</Color>
<Color x:Key="CpuBgTint">#103B82F6</Color>

<!-- MEMORY COLORS -->
<Color x:Key="MemoryColor">#A855F7</Color>
<Color x:Key="MemoryColorLight">#C084FC</Color>
<Color x:Key="MemoryColorDark">#9333EA</Color>
<Color x:Key="MemoryBgTint">#10A855F7</Color>

<!-- GPU COLORS (Future) -->
<Color x:Key="GpuColor">#10B981</Color>
<Color x:Key="GpuColorLight">#34D399</Color>
<Color x:Key="GpuColorDark">#059669</Color>
<Color x:Key="GpuBgTint">#1010B981</Color>
```

### Component Specifications

#### 1. Circular Gauge Component

For compact system metric display in header strip.

```
    ╭───────╮
   ╱  ▁▂▃▄  ╲
  │    78%   │
   ╲   CPU  ╱
    ╰───────╯
```

**Properties:**
- Size: 40px (compact), 64px (card), 96px (detail)
- Ring thickness: 4px (compact), 6px (card)
- Colors: Progress uses metric color, background uses `SurfaceColor`
- Animation: Value change with eased interpolation (300ms)

#### 2. Mini Sparkline

Inline trend visualization for quick pattern recognition.

```
┌────────────────────────┐
│ ▁▂▃▅▆▇█▇▆▅▃▂▁▂▃▅▆▇█  │
└────────────────────────┘
```

**Properties:**
- Height: 20px
- Width: 80-120px (flexible)
- Points: Last 30-60 data points
- Stroke: 1.5px, metric color
- Fill: Gradient from metric color (20% opacity) to transparent

#### 3. Metric Card (Unified)

Consistent card design for all metrics.

```
╔══════════════════════════════════════════╗
║ ┌────┐                                   ║
║ │ ↓  │  DOWNLOAD                  ↗ 12% ║
║ └────┘                                   ║
║                                          ║
║       124.5 MB/s                         ║
║       ▁▂▃▅▆▇█▇▆▅▃▂▁ (sparkline)         ║
║                                          ║
║  Today: 2.4 GB    Peak: 245 MB/s         ║
╚══════════════════════════════════════════╝
```

**Variants:**
- `MetricCard.Network` - Full size, hero display
- `MetricCard.System` - Medium size, secondary display
- `MetricCard.Compact` - Minimal, header strip

#### 4. Layer Toggle Button

For chart overlays.

```
┌─────────────┐  ┌─────────────┐
│ ✓ Download  │  │ ○ CPU       │
│ ━━━━━━━━━━  │  │ ┄┄┄┄┄┄┄┄┄┄  │
└─────────────┘  └─────────────┘
   (active)        (inactive)
```

**States:**
- Active: Filled checkbox, colored line preview, metric color text
- Inactive: Hollow checkbox, muted line preview, secondary text
- Hover: Slight background highlight

---

## Component Hierarchy

### New Components

```
src/WireBound.Avalonia/
├── Controls/
│   ├── CircularGauge.axaml(.cs)          # Circular progress gauge
│   ├── MiniSparkline.axaml(.cs)          # Inline trend chart
│   ├── MetricCard.axaml(.cs)             # Unified metric display card
│   ├── LayerToggle.axaml(.cs)            # Chart layer toggle button
│   ├── SystemHealthStrip.axaml(.cs)      # Compact system metrics bar
│   └── CorrelationMatrix.axaml(.cs)      # Correlation heatmap display
```

### ViewModel Structure

```
ViewModels/
├── OverviewViewModel.cs                   # Unified dashboard (replaces Dashboard)
│   ├── NetworkMetrics (embedded)
│   ├── SystemMetrics (embedded)
│   └── QuickSystemStrip (embedded)
│
├── LiveChartsViewModel.cs                 # Multi-metric charting (enhanced Charts)
│   ├── ChartLayerManager
│   └── TimeRangeSelector
│
├── InsightsViewModel.cs                   # Unified history/stats (replaces History)
│   ├── NetworkUsageTab
│   ├── SystemTrendsTab
│   ├── CorrelationsTab
│   └── ExportTab
│
├── ApplicationsViewModel.cs               # Unchanged
├── ConnectionsViewModel.cs                # Unchanged
└── SettingsViewModel.cs                   # Add customization options
```

### Service Layer Additions

```
Services/
├── ISystemHistoryService.cs               # Historical system data
│   ├── SaveSystemStatsAsync()
│   ├── GetHourlySystemStatsAsync()
│   └── GetDailySystemStatsAsync()
│
├── ICorrelationService.cs                 # Metric correlation analysis
│   ├── CalculateCorrelation()
│   └── GetInsights()
│
└── IExportService.cs                      # Data export
    ├── ExportToCsv()
    ├── ExportToJson()
    └── GeneratePdfReport()
```

### Database Additions (EF Core)

```csharp
// New entities for system history
public class HourlySystemStats
{
    public int Id { get; set; }
    public DateTime Hour { get; set; }
    public double AvgCpuPercent { get; set; }
    public double MaxCpuPercent { get; set; }
    public double AvgMemoryPercent { get; set; }
    public double MaxMemoryPercent { get; set; }
    public double? AvgGpuPercent { get; set; }
}

public class DailySystemStats
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public double AvgCpuPercent { get; set; }
    public double MaxCpuPercent { get; set; }
    public double AvgMemoryPercent { get; set; }
    public double MaxMemoryPercent { get; set; }
}
```

---

## UX Enhancements

### 1. Progressive Disclosure

| Level | What's Shown | User Action |
|-------|-------------|-------------|
| **Glance** | Quick system strip (CPU/RAM %) | Default view |
| **Summary** | System Health card with gauges | Click strip or scroll |
| **Detail** | Full charts, per-core, temps | Click "Expand" or navigate |

### 2. Customization

Settings → Dashboard → Customize

- [ ] Show system metrics in header
- [ ] Auto-expand System Health card
- [ ] Show CPU overlay on network chart
- [ ] Show Memory overlay on network chart
- [ ] Preferred time range (1m, 5m, 15m, 1h)
- [ ] Enable correlation insights
- [ ] GPU monitoring (when available)

### 3. Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `1-5` | Switch between main pages |
| `C` | Toggle CPU overlay |
| `M` | Toggle Memory overlay |
| `T` | Cycle time ranges |
| `F` | Fullscreen chart |
| `R` | Reset chart zoom |

### 4. Accessibility

- All gauges have text alternatives
- Color + icon for status (not just color)
- High contrast mode support
- Screen reader announcements for live data updates
- Keyboard navigation throughout

### 5. Performance Mode

Toggle in settings:
- Reduces chart update frequency (2s → 5s)
- Disables sparkline animations
- Simplifies gauge rendering
- Removes background blur effects

---

## Migration Path

### Phase 1: Foundation (Week 1-2)
1. Create new color resources for CPU/Memory/GPU
2. Build CircularGauge and MiniSparkline controls
3. Add SystemHealthStrip component
4. Update database schema for system history

### Phase 2: Overview Page (Week 2-3)
1. Create OverviewViewModel combining network + system
2. Build unified Overview page layout
3. Integrate SystemHealthStrip into header
4. Add chart layer toggle support

### Phase 3: Live Charts Enhancement (Week 3-4)
1. Enhance ChartsViewModel with multi-metric support
2. Add dual Y-axis support for overlays
3. Implement LayerToggle component
4. Time range selector improvements

### Phase 4: Insights Page (Week 4-5)
1. Create InsightsViewModel with tabs
2. Build Network Usage tab (enhanced History)
3. Build System Trends tab
4. Implement Correlations tab with analysis

### Phase 5: Polish & Integration (Week 5-6)
1. Update navigation (merge routes)
2. Add customization settings
3. Performance optimization
4. Accessibility audit
5. Animation polish

---

## Future Considerations

### GPU Monitoring
- NVIDIA: NVML library via P/Invoke
- AMD: ADL library
- Intel: IGC metrics
- Cross-platform: Stub for unsupported

### Extended Insights
- ML-based anomaly detection
- Usage predictions
- Optimization recommendations
- "Quiet hours" detection

### Widgets
- Detachable mini-widgets for desktop overlay
- Multi-monitor support
- Always-on-top compact mode

---

## Appendix A: Competitive Analysis

| Feature | WireBound | Task Manager | iStat Menus | btop++ |
|---------|-----------|--------------|-------------|--------|
| Network primary | ✓ | ✗ | ✗ | ✗ |
| CPU/RAM | ✓ | ✓ | ✓ | ✓ |
| GPU | Planned | ✓ | ✓ | ✓ |
| Historical data | ✓ | ✗ | Limited | ✗ |
| Correlation | ✓ | ✗ | ✗ | ✗ |
| Per-app network | ✓ | Limited | ✗ | ✗ |
| Cross-platform | ✓ | ✗ | ✗ | ✓ |
| Modern UI | ✓ | ✓ | ✓ | ✗ |

### WireBound Differentiators
1. **Network-first** with system context
2. **Correlation insights** between metrics
3. **Historical analysis** with export
4. **Cross-platform** with native feel
5. **Modern "Fluid Data"** design language

---

## Appendix B: Mockup Color Reference

```
Network Download: #00E5FF (Electric Cyan)
Network Upload:   #FF6B35 (Coral Orange)
CPU:              #3B82F6 (Sapphire Blue)
Memory:           #A855F7 (Amethyst Purple)
GPU:              #10B981 (Emerald Green)

Background:       #0D1321 (Deep Navy)
Surface:          #1D2D44 (Ocean Blue)
Card:             #1D2D44 (Ocean Blue)
Text Primary:     #F0EBD8 (Warm White)
Text Secondary:   #A0A8B8 (Soft Gray)
```

---

*Document Version: 1.0*
*Last Updated: January 2026*
*Author: WireBound Design Team*
