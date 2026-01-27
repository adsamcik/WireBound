# Development Guide

## Prerequisites

- **Windows 10/11, Linux, or macOS**
- **.NET 10 SDK** - [Download](https://dotnet.microsoft.com/download)
- **Visual Studio 2022** or **VS Code with C# Dev Kit**
- **Git**

## Setup

```powershell
# Clone the repository
git clone https://github.com/adsamcik/wire-bound.git
cd wire-bound

# Restore dependencies
dotnet restore

# Build all projects
dotnet build

# Run the application
dotnet run --project src/WireBound.Avalonia/WireBound.Avalonia.csproj
```

## Project Structure

```
wire-bound/
├── src/
│   ├── WireBound.Avalonia/      # Main UI application
│   ├── WireBound.Core/          # Shared library (models, interfaces, helpers)
│   ├── WireBound.Platform.Abstract/  # Platform interfaces
│   ├── WireBound.Platform.Windows/   # Windows implementations
│   ├── WireBound.Platform.Linux/     # Linux implementations
│   └── WireBound.Platform.Stub/      # Fallback implementations
├── tests/
│   └── WireBound.Tests/         # Unit tests
├── docs/                        # Design documentation
├── scripts/                     # Build/publish scripts
└── publish/                     # Published artifacts
```

## Development Workflow

### Running Locally

```powershell
# Standard run
dotnet run --project src/WireBound.Avalonia/WireBound.Avalonia.csproj

# Hot reload (watch mode)
dotnet watch run --project src/WireBound.Avalonia/WireBound.Avalonia.csproj
```

### Running Tests

```powershell
# All tests
dotnet test

# Specific test file
dotnet test --filter "FullyQualifiedName~OverviewViewModelTests"

# With coverage (requires coverlet)
dotnet test --collect:"XPlat Code Coverage"
```

### Debugging

#### VS Code
1. Open the workspace
2. Press `F5` to start debugging
3. Select ".NET Core Launch" configuration

#### Visual Studio
1. Open `WireBound.slnx`
2. Set `WireBound.Avalonia` as startup project
3. Press `F5`

## Build & Publish

### Development Build

```powershell
dotnet build
```

### Release Build

```powershell
dotnet build -c Release
```

### Publish for Distribution

```powershell
# Windows x64
.\scripts\publish.ps1 -Version "1.0.0" -Runtime win-x64

# Linux x64
.\scripts\publish.ps1 -Version "1.0.0" -Runtime linux-x64

# macOS ARM64
.\scripts\publish.ps1 -Version "1.0.0" -Runtime osx-arm64

# All platforms
.\scripts\publish.ps1 -Version "1.0.0"
```

## Common Tasks

### Adding a New View

1. **Create ViewModel** in `src/WireBound.Avalonia/ViewModels/`:

```csharp
public partial class NewFeatureViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "New Feature";
}
```

2. **Create View** in `src/WireBound.Avalonia/Views/`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             x:Class="WireBound.Avalonia.Views.NewFeatureView">
    <TextBlock Text="{Binding Title}"/>
</UserControl>
```

3. **Add Route** to `src/WireBound.Core/Routes.cs`:

```csharp
public const string NewFeature = "NewFeature";
```

4. **Register in DI** in `App.axaml.cs`:

```csharp
services.AddSingleton<NewFeatureViewModel>();
services.AddTransient<NewFeatureView>();
```

5. **Add to ViewFactory** in `ViewFactory.cs`

6. **Add to Navigation** in `MainViewModel.cs`

### Adding a Platform-Specific Feature

1. **Define interface** in `WireBound.Platform.Abstract/Services/`:

```csharp
public interface INewProvider
{
    Task<string> GetDataAsync();
}
```

2. **Create stub** in `WireBound.Platform.Stub/Services/`:

```csharp
public sealed class StubNewProvider : INewProvider
{
    public Task<string> GetDataAsync() => Task.FromResult("N/A");
}
```

3. **Create Windows impl** in `WireBound.Platform.Windows/Services/`:

```csharp
[SupportedOSPlatform("windows")]
public sealed class WindowsNewProvider : INewProvider
{
    public async Task<string> GetDataAsync()
    {
        // Windows-specific code
    }
}
```

4. **Create Linux impl** in `WireBound.Platform.Linux/Services/`:

```csharp
[SupportedOSPlatform("linux")]
public sealed class LinuxNewProvider : INewProvider
{
    public async Task<string> GetDataAsync()
    {
        // Linux-specific code (e.g., read /proc)
    }
}
```

5. **Register in platform services**:
   - `StubPlatformServices.Register()` - Add stub
   - `WindowsPlatformServices.Register()` - Add Windows impl
   - `LinuxPlatformServices.Register()` - Add Linux impl

### Adding a Database Table

1. **Create model** in `WireBound.Core/Models/`:

```csharp
public sealed class NewRecord
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string Data { get; set; } = string.Empty;
}
```

2. **Add DbSet** to `WireBoundDbContext`:

```csharp
public DbSet<NewRecord> NewRecords { get; set; } = null!;
```

3. **Add migration** in `ApplyMigrations()` if needed

4. **Add repository interface** and implementation

### Adding Styles

1. **Colors** go in `src/WireBound.Avalonia/Styles/Colors.axaml`
2. **Spacing/sizing** go in `Styles.axaml`
3. **Reference design system** in `docs/DESIGN_SYSTEM.md`

## Environment Variables

| Variable | Purpose | Required | Default |
|----------|---------|----------|---------|
| None currently | - | - | - |

Database location: `%LOCALAPPDATA%/WireBound/wirebound.db` (Windows) or `~/.local/share/WireBound/wirebound.db` (Linux)

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Build fails with analyzer errors | Run `dotnet restore` then `dotnet build` |
| Database locked | Close other instances of the app |
| Charts not updating | Ensure `LiveCharts.Configure()` called in `App.Initialize()` |
| Platform service not found | Check platform registration order in `App.ConfigureServices()` |
| Tests fail with LiveCharts errors | Ensure test is in `[Collection("LiveCharts")]` |
| Network stats showing zero | Check if network adapter is selected in settings |

## Code Quality

### Linting

The project uses built-in .NET analyzers. No additional linter setup required.

### Code Style

Follow patterns in `.github/context/PATTERNS.md`

### Pre-commit Checklist

- [ ] Code compiles: `dotnet build`
- [ ] Tests pass: `dotnet test`
- [ ] No new warnings
- [ ] Accessibility added to new UI elements
- [ ] Stub implementations for new platform interfaces
