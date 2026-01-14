# WireBound Design System v2.0

## Design Vision: "Fluid Data"

WireBound's redesign embodies the concept of **data flowing through digital wires**. The visual language combines deep, oceanic colors with subtle glassmorphism effects, creating a premium monitoring experience that feels both modern and functional.

---

## Color Palette: "Deep Ocean"

### Primary Colors

| Role | Color | Hex | Usage |
|------|-------|-----|-------|
| Background | Deep Navy | `#0D1321` | Main app background |
| Surface | Ocean Blue | `#1D2D44` | Cards, panels, elevated surfaces |
| Surface Elevated | Slate | `#3E5C76` | Hover states, elevated cards |

### Brand & Activity Colors

| Role | Color | Hex | Usage |
|------|-------|-----|-------|
| Download | Electric Cyan | `#00E5FF` | Download speeds, primary actions |
| Download Glow | Bright Cyan | `#00D4FF` | Chart lines, accents |
| Upload | Coral Orange | `#FF6B35` | Upload speeds (high contrast) |
| Upload Alt | Mint Green | `#00FF88` | Alternative for charts |

### Text Colors

| Role | Color | Hex | Usage |
|------|-------|-----|-------|
| Primary Text | Warm White | `#F0EBD8` | Headlines, important data |
| Secondary Text | Soft Gray | `#A0A8B8` | Labels, descriptions |
| Muted Text | Dim Gray | `#6B7280` | Timestamps, hints |

### Status Colors

| Role | Color | Hex | Usage |
|------|-------|-----|-------|
| Success | Mint | `#00C9A7` | Active status, success states |
| Warning | Amber | `#FFB627` | Alerts, cautions |
| Error | Coral Red | `#F45B69` | Errors, disconnected |

### Surface & Border Colors

| Role | Color | Hex | Usage |
|------|-------|-----|-------|
| Card Background | Deep Blue (60%) | `#990F3460` | Glass card backgrounds |
| Card Border | Soft White | `#20FFFFFF` | Subtle glass borders |
| Divider | Ocean Dark | `#2A3A5E` | Separators, lines |
| Rail Background | True Dark | `#0A0E14` | Navigation rail |

---

## Typography

### Font Stack
- **Primary:** Segoe UI Variable (Windows 11 native)
- **Monospace:** Cascadia Mono (for technical data)

### Scale

| Style | Weight | Size | Line Height | Usage |
|-------|--------|------|-------------|-------|
| Display | SemiBold | 48px | 1.1 | Hero speed numbers |
| Title 1 | Bold | 32px | 1.2 | Page headers |
| Title 2 | SemiBold | 24px | 1.3 | Section headers |
| Title 3 | SemiBold | 18px | 1.4 | Card headers |
| Body | Regular | 14px | 1.5 | General text |
| Caption | Medium | 12px | 1.4 | Labels, metadata |
| Micro | Regular | 11px | 1.3 | Timestamps |

---

## Component Specifications

### 1. Glass Card

A frosted glass container for content groupings.

**Properties:**
- Background: `#990F3460` (semi-transparent deep blue)
- Border: 1px `#20FFFFFF`
- Border Radius: 16px
- Padding: 24px (large), 20px (medium), 16px (small)
- Shadow: `0 4px 20px #20000000`

**Hover State:**
- TranslateY: -2px
- Shadow increases: `0 8px 30px #30000000`
- Border brightness: +5%

### 2. Navigation Rail

Collapsible sidebar with wire/node visual metaphor.

**States:**
- Collapsed: 64px width (icons only)
- Expanded: 240px width (icons + labels)

**Visual Elements:**
- Vertical wire line: 2px `#2A3A5E`
- Node circles: 8px diameter at each menu item
- Active node: Filled `#00E5FF` with glow
- Inactive node: Hollow `#3E5C76`

### 3. Speed Metric Card

Enhanced display for download/upload speeds.

**Layout:**
- Direction icon (animated arrow)
- Speed value (48px, gradient text)
- Unit label (12px, secondary)
- Mini sparkline (60-second history)
- Progress bar showing % of peak

**Colors:**
- Download gradient: `#00E5FF` → `#00D4FF`
- Upload gradient: `#FF6B35` → `#FF8C5A`

### 4. Status Indicator

Animated pulse showing monitoring state.

**States:**
- Active: Green (`#00C9A7`) with pulsing animation
- Idle: Amber (`#FFB627`) with slow fade
- Error: Red (`#F45B69`) with fast blink

**Animation:**
- Scale: 1.0 → 1.3 → 1.0
- Opacity: 1.0 → 0.6 → 1.0
- Duration: 1.5s loop

### 5. Bento Grid Layout

Responsive dashboard grid system.

**Breakpoints:**
- Wide (≥1200px): 3-column bento
- Medium (800-1199px): 2-column grid
- Narrow (<800px): Single column stack

**Grid Template (Wide):**
```
┌────────────────────┬───────────────┐
│   Download Gauge   │   Session     │
│      (2×1)         │   Stats (1×2) │
├────────────────────┤               │
│   Upload Gauge     │               │
│      (2×1)         ├───────────────┤
├────────────────────┴───────────────┤
│         Live Chart (3×1)           │
└────────────────────────────────────┘
```

---

## Animation Guidelines

### Micro-interactions

| Element | Trigger | Animation | Duration | Easing |
|---------|---------|-----------|----------|--------|
| Card | Hover | Lift + glow | 200ms | ease-out |
| Speed value | Update | Counter roll | 300ms | ease-out-quart |
| Chart point | Add | Fade + rise | 200ms | ease-out |
| Page | Navigate | Slide + fade | 300ms | ease-in-out-cubic |
| Status dot | Loop | Pulse scale | 1500ms | sin-in-out |

### Performance Mode
When enabled, disable:
- Blur effects
- Complex shadows
- Background animations

---

## Iconography

### Navigation Icons (Unicode/Emoji fallback)
- Dashboard: 📊 or custom SVG
- Live Chart: 📈 or custom SVG
- Applications: 📱 or custom SVG
- History: 📅 or custom SVG
- Settings: ⚙️ or custom SVG

### Activity Icons
- Download: ↓ (styled arrow)
- Upload: ↑ (styled arrow)
- Network: 🌐 (globe)
- Wire: Custom flowing wire SVG

---

## Spacing Scale

| Token | Value | Usage |
|-------|-------|-------|
| xs | 4px | Tight spacing |
| sm | 8px | Related elements |
| md | 16px | Section spacing |
| lg | 24px | Major sections |
| xl | 32px | Page padding |
| 2xl | 48px | Hero spacing |

---

## Visual Metaphors

### "WireBound" Identity
The name suggests connectivity—being bound to the network. Visual reinforcement:

1. **Wire traces**: Subtle line patterns in backgrounds
2. **Node connections**: Menu items connected by lines
3. **Data flow**: Chart lines with glowing trails
4. **Pulse animations**: Heartbeat-like status indicators

### Background Treatment
- Subtle radial gradient from center
- Optional: Faint circuit trace pattern overlay
- Color: `#0D1321` → `#151F2E` gradient

---

## Implementation Checklist

- [x] Document design system
- [ ] Update Colors.xaml with new palette
- [ ] Update Styles.xaml with glass components
- [ ] Create NavigationRailView control
- [ ] Create SpeedMetricCard control
- [ ] Create StatusIndicator control
- [ ] Redesign DashboardPage layout
- [ ] Redesign ChartsPage layout
- [ ] Add animations with Avalonia animations
- [ ] Test responsive breakpoints
- [ ] Performance optimization pass
