<!--
context-init:version: 3.0.0
context-init:generated: 2026-02-07T14:21:00Z
context-init:mode: full-init
context-init:sections:
  - id: overview, type: managed
  - id: build, type: managed
  - id: tech-stack, type: managed
  - id: architecture, type: managed
  - id: key-patterns, type: managed
  - id: code-conventions, type: managed
  - id: important-files, type: managed
  - id: gotchas, type: managed
  - id: agent-instructions, type: managed
-->

# WireBound

<!-- context-init:managed -->
Privacy-focused, cross-platform network traffic and system monitoring application built with .NET 10 and Avalonia UI. Provides real-time network speed monitoring, per-application tracking, CPU/RAM monitoring, and historical data visualization—all stored locally with no cloud telemetry.

## Build & Development

<!-- context-init:managed -->
```powershell
# Install dependencies
dotnet restore

# Build all projects
dotnet build

# Run the application
dotnet run --project src/WireBound.Avalonia/WireBound.Avalonia.csproj

# Run tests
dotnet run --project tests/WireBound.Tests/WireBound.Tests.csproj

# Publish (all platforms)
.\scripts\publish.ps1 -Version "1.0.0"

# Publish (specific platform)
.\scripts\publish.ps1 -Version "1.0.0" -Runtime linux-x64
```

## Tech Stack

<!-- context-init:managed -->
- **Language**: C# / .NET 10 (SDK 10.0.102)
- **UI**: Avalonia 11.3.11 (cross-platform — Windows, Linux)
- **Architecture**: MVVM with CommunityToolkit.Mvvm 8.4.0
- **DI**: Microsoft.Extensions.DependencyInjection 10.0.2
- **Database**: SQLite with Entity Framework Core 10.0.2
- **Charts**: LiveChartsCore.SkiaSharpView.Avalonia 2.0.0-rc6.1
- **Logging**: Serilog 4.3.0 (file sink, daily rolling, 14-day retention)
- **Testing**: TUnit, NSubstitute, AwesomeAssertions, EF Core InMemory

## Architecture

<!-- context-init:managed -->
```text
┌─────────────────────────────────────────────────────────────────────┐
│                        WireBound.Avalonia                           │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌────────────┐│
│  │    Views    │  │  ViewModels │  │  Services   │  │  Controls  ││
│  └─────────────┘  └─────────────┘  └─────────────┘  └────────────┘│
└──────────────────────────────┬──────────────────────────────────────┘
                               │
┌──────────────────────────────┼──────────────────────────────────────┐
│                    WireBound.Core                                    │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌────────────┐│
│  │   Models    │  │  Services   │  │    Data     │  │  Helpers   ││
│  │             │  │ (Interfaces)│  │ (DbContext) │  │            ││
│  └─────────────┘  └─────────────┘  └─────────────┘  └────────────┘│
└──────────────────────────────┬──────────────────────────────────────┘
                               │
┌──────────────────────────────┼──────────────────────────────────────┐
│               WireBound.Platform.Abstract                            │
│  ┌────────────────────────────────────────────────────────────────┐ │
│  │  Platform Interfaces (ICpuInfoProvider, IMemoryInfoProvider,   │ │
│  │  IProcessNetworkProvider, IStartupService, IElevationService)  │ │
│  └────────────────────────────────────────────────────────────────┘ │
└──────────────────────────────┬──────────────────────────────────────┘
           ┌───────────────────┼───────────────────┐
           ▼                   ▼                   ▼
┌──────────────────┐  ┌──────────────────┐  ┌──────────────────┐
│ Platform.Windows │  │  Platform.Linux  │  │  Platform.Stub   │
│  (Windows APIs)  │  │ (/proc, nmcli)   │  │   (Fallbacks)    │
└──────────────────┘  └──────────────────┘  └──────────────────┘
```

See @.github/context/ARCHITECTURE.md for detailed component maps and data flows.

## Key Patterns

<!-- context-init:managed -->
1. **MVVM with Source Generators**: ViewModels inherit `ObservableObject`, use `[ObservableProperty]` and `[RelayCommand]`
2. **Platform Service Factory**: Each platform project has `*PlatformServices.cs` registering platform implementations; stubs register first, platform overrides second
3. **Interface Segregation**: `DataPersistenceService` registered once, exposed via multiple interfaces (`INetworkUsageRepository`, `IAppUsageRepository`, `ISettingsRepository`, `ISpeedSnapshotRepository`)
4. **Routes as Constants**: Navigation uses `Routes` class with 7 named routes
5. **View Factory Pattern**: Views created via `IViewFactory` for DI; ViewModels are singletons, Views are transient
6. **DateTime Convention**: Use `DateTime.Now` for user-facing aggregations (local time), `DateTime.UtcNow` only for internal cache TTL

## Code Conventions

<!-- context-init:managed -->
- **File naming**: PascalCase matching class name
- **Interfaces**: `I` prefix (e.g., `INetworkMonitorService`)
- **ViewModels**: `...ViewModel` suffix, **Views**: `...View` / `...Window` suffix
- **Platform services**: Platform prefix (e.g., `WindowsCpuInfoProvider`, `LinuxMemoryInfoProvider`)
- **Tests**: `...Tests` suffix (e.g., `OverviewViewModelTests`)
- **Private fields**: `_camelCase`, **Async methods**: `...Async` suffix
- **Constants**: PascalCase (not UPPER_SNAKE)

See @.github/context/PATTERNS.md for detailed examples.

## Important Files

<!-- context-init:managed -->
| File | Purpose |
|------|---------|
| `src/WireBound.Avalonia/App.axaml.cs` | Application entry, all DI registration |
| `src/WireBound.Avalonia/Program.cs` | Serilog configuration, app bootstrap |
| `src/WireBound.Core/Data/WireBoundDbContext.cs` | EF Core context, migrations, schema |
| `src/WireBound.Core/Routes.cs` | Navigation route constants (7 routes) |
| `src/WireBound.Platform.Abstract/IPlatformServices.cs` | Platform service factory interface |
| `src/WireBound.Avalonia/Styles/Colors.axaml` | Design system color definitions |
| `src/WireBound.Avalonia/Services/ViewFactory.cs` | View creation with DI |
| `docs/DESIGN_SYSTEM.md` | Complete UI/UX design guidelines |
| `Directory.Packages.props` | Central package version management |

## Gotchas

<!-- context-init:managed -->
- **Test framework is TUnit + NSubstitute** — not xUnit/Moq. Use `[Test]` not `[Fact]`, use `NSubstitute.Substitute.For<T>()` not `new Mock<T>()`
- **Database migrations**: Use `context.ApplyMigrations()` for manual schema updates; standard EF migrations are NOT used
- **Platform annotations required**: Must annotate with `[SupportedOSPlatform("windows")]` or `[SupportedOSPlatform("linux")]`
- **LiveCharts thread safety**: Chart updates must happen on UI thread via `Dispatcher.UIThread`
- **Stub implementations required**: Every platform interface needs a stub in `WireBound.Platform.Stub`
- **Singleton ViewModels**: Most ViewModels are singletons; Views are transient — don't store view-specific state in ViewModels that should reset
- **Platform registration order**: Stubs register first as defaults, then platform-specific implementations override them
- **LiveCharts in tests**: Tests touching LiveCharts need the `LiveChartsHook` assembly hook for initialization
- **Central package management**: Package versions are in `Directory.Packages.props`, not individual `.csproj` files
- **Log files**: Written to `LocalApplicationData/WireBound/logs/` with 14-day retention

## Agent Instructions

<!-- context-init:managed -->
- Run tests after changes: `dotnet run --project tests/WireBound.Tests/WireBound.Tests.csproj`
- Follow platform abstraction: new platform features need interfaces in `Platform.Abstract`, implementations in `Platform.Windows` and `Platform.Linux`, stubs in `Platform.Stub`
- Use existing helpers: `ByteFormatter`, `ChartColors`, `CircularBuffer`, `LttbDownsampler`, `AdaptiveThresholdCalculator`, `TrendIndicatorCalculator` in `WireBound.Core/Helpers`
- Register all services in `App.axaml.cs` `ConfigureServices()` method
- Follow design system: colors from `Colors.axaml`, spacing from `docs/DESIGN_SYSTEM.md`
- Add `AutomationProperties` to new interactive UI elements
- Use conventional commits: `type(scope): description`
- Follow patterns in @.github/context/PATTERNS.md
- Check @.github/context/DEVELOPMENT.md for environment setup

<!-- context-init:user-content-below -->
<!-- Add custom instructions below this line -->
