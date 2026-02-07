<!--
context-init:version: 3.0.0
context-init:generated: 2026-02-07T14:21:00Z
context-init:mode: full-init
-->

# Development Guide

## Prerequisites

<!-- context-init:managed -->

- **OS**: Windows 10/11 or Linux
- **.NET 10 SDK** (10.0.102) — [Download](https://dotnet.microsoft.com/download)
- **IDE**: Visual Studio 2022, VS Code with C# Dev Kit, or JetBrains Rider
- **Git**

## Setup

<!-- context-init:managed -->

```powershell
git clone https://github.com/adsamcik/wire-bound.git
cd wire-bound
dotnet restore
dotnet build
dotnet run --project src/WireBound.Avalonia/WireBound.Avalonia.csproj
```

## Project Structure

<!-- context-init:managed -->

```
wire-bound/
├── src/
│   ├── WireBound.Avalonia/          # Main UI application (WinExe)
│   │   ├── Controls/                # CircularGauge, MiniSparkline, SystemHealthStrip
│   │   ├── Converters/              # SelectedRowConverter, SpeedUnitConverter
│   │   ├── Helpers/                 # ChartSeriesFactory, ChartDataManager
│   │   ├── Services/                # 14 service implementations
│   │   ├── Styles/                  # Colors.axaml, Styles.axaml
│   │   ├── ViewModels/              # 8 ViewModels + AdapterDisplayItem
│   │   └── Views/                   # 8 Views (AXAML + code-behind)
│   ├── WireBound.Core/              # Shared library
│   │   ├── Data/                    # WireBoundDbContext
│   │   ├── Helpers/                 # 6 utility classes
│   │   ├── Models/                  # 17 domain models
│   │   ├── Services/                # 14 service interfaces
│   │   └── Routes.cs                # Navigation route constants
│   ├── WireBound.Platform.Abstract/ # Platform interfaces (10) + models (5)
│   ├── WireBound.Platform.Windows/  # Windows implementations (7 services)
│   ├── WireBound.Platform.Linux/    # Linux implementations (7 services)
│   └── WireBound.Platform.Stub/     # Stub fallbacks (7 services)
├── tests/
│   └── WireBound.Tests/             # Unit tests (TUnit + NSubstitute)
│       ├── Fixtures/                # DatabaseTestBase, LiveChartsHook
│       ├── Helpers/                 # Helper class tests
│       ├── Models/                  # Model tests
│       ├── Services/                # Service tests
│       └── ViewModels/              # ViewModel tests (8 files)
├── docs/                            # Design docs, limitations, publishing
├── scripts/                         # Build and publish scripts
└── publish/                         # Published artifacts
```

## Common Commands

<!-- context-init:managed -->

```powershell
# Restore dependencies
dotnet restore

# Build all projects
dotnet build

# Build release
dotnet build -c Release

# Run the application
dotnet run --project src/WireBound.Avalonia/WireBound.Avalonia.csproj

# Run with hot reload
dotnet watch run --project src/WireBound.Avalonia/WireBound.Avalonia.csproj

# Run all tests
dotnet test

# Run specific test class
dotnet test --filter "FullyQualifiedName~OverviewViewModelTests"

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"

# Publish all platforms
.\scripts\publish.ps1 -Version "1.0.0"

# Publish specific platform
.\scripts\publish.ps1 -Version "1.0.0" -Runtime win-x64
.\scripts\publish.ps1 -Version "1.0.0" -Runtime linux-x64
```

## Common Tasks

<!-- context-init:managed -->

### Adding a New View

1. Create ViewModel in `src/WireBound.Avalonia/ViewModels/`:
   ```csharp
   public partial class NewFeatureViewModel : ObservableObject
   {
       [ObservableProperty]
       private string _title = "New Feature";
   }
   ```

2. Create View in `src/WireBound.Avalonia/Views/` (AXAML + code-behind)

3. Add route to `src/WireBound.Core/Routes.cs`:
   ```csharp
   public const string NewFeature = "NewFeature";
   ```

4. Register in DI (`App.axaml.cs`):
   ```csharp
   services.AddSingleton<NewFeatureViewModel>();
   services.AddTransient<NewFeatureView>();
   ```

5. Add to `ViewFactory.cs` route→view mapping

6. Add to `MainViewModel.cs` navigation items

### Adding a Platform-Specific Feature

1. Define interface in `WireBound.Platform.Abstract/Services/`
2. Create stub in `WireBound.Platform.Stub/Services/`
3. Create Windows impl in `WireBound.Platform.Windows/Services/` with `[SupportedOSPlatform("windows")]`
4. Create Linux impl in `WireBound.Platform.Linux/Services/` with `[SupportedOSPlatform("linux")]`
5. Register in all three `*PlatformServices.Register()` methods

### Adding a Database Table

1. Create model in `WireBound.Core/Models/`
2. Add `DbSet<T>` to `WireBoundDbContext`
3. Add migration logic in `ApplyMigrations()` if schema changes
4. Add repository interface and implementation

### Adding Styles

1. Colors → `src/WireBound.Avalonia/Styles/Colors.axaml`
2. Spacing/sizing → `src/WireBound.Avalonia/Styles/Styles.axaml`
3. Reference `docs/DESIGN_SYSTEM.md` for the design system

## Environment Variables

<!-- context-init:managed -->

| Variable | Purpose | Required | Default |
|----------|---------|----------|---------|
| *(none currently)* | — | — | — |

### File Locations

| File | Windows | Linux |
|------|---------|-------|
| Database | `%LOCALAPPDATA%\WireBound\wirebound.db` | `~/.local/share/WireBound/wirebound.db` |
| Logs | `%LOCALAPPDATA%\WireBound\logs\` | `~/.local/share/WireBound/logs/` |

## Debugging

<!-- context-init:managed -->

### VS Code
1. Open workspace
2. Press `F5` → select ".NET Core Launch"

### Visual Studio
1. Open `WireBound.slnx`
2. Set `WireBound.Avalonia` as startup project
3. Press `F5`

### Rider
1. Open `WireBound.slnx`
2. Run `WireBound.Avalonia` configuration

## Troubleshooting

<!-- context-init:managed -->

| Issue | Solution |
|-------|----------|
| Build fails with analyzer errors | Run `dotnet restore` then `dotnet build` |
| Database locked | Close other instances of the app |
| Charts not updating | Ensure `LiveCharts.Configure()` called in `App.Initialize()` |
| Platform service not found | Check registration order: stubs → platform override in `App.ConfigureServices()` |
| Tests fail with LiveCharts errors | Ensure `LiveChartsHook` assembly hook is present |
| Network stats showing zero | Check if network adapter is selected in settings |
| Package version conflicts | Versions are central in `Directory.Packages.props` — don't add versions in `.csproj` |
| Schema migration errors | Check `ApplyMigrations()` in `WireBoundDbContext` |
| UI thread exceptions | Wrap chart/UI updates in `Dispatcher.UIThread.InvokeAsync()` |

## Code Quality

<!-- context-init:managed -->

- Built-in .NET analyzers (no additional linter setup)
- Trimming analyzers enabled (`IsTrimmable=true`)
- Code style enforcement via analyzers
- Follow patterns in `.github/context/PATTERNS.md`

### Pre-commit Checklist

1. `dotnet build` — no errors or new warnings
2. `dotnet test` — all tests pass
3. Accessibility: `AutomationProperties` on new interactive elements
4. Stub implementations for any new platform interfaces
5. Conventional commit message: `type(scope): description`

<!-- context-init:user-content-below -->
