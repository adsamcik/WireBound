# Contributing to WireBound

Thank you for your interest in contributing to WireBound! This guide will help you get started.

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Git

### Building

```powershell
# Clone the repository
git clone https://github.com/AdenMck/wire-bound.git
cd wire-bound

# Restore dependencies
dotnet restore

# Build all projects
dotnet build

# Run the application
dotnet run --project src/WireBound.Avalonia/WireBound.Avalonia.csproj
```

### Running Tests

```powershell
dotnet run --project tests/WireBound.Tests/WireBound.Tests.csproj
```

## Project Structure

```
src/
├── WireBound.Core/              # Shared models, services interfaces, helpers
├── WireBound.Platform.Abstract/ # Platform abstraction interfaces
├── WireBound.Platform.Windows/  # Windows-specific implementations
├── WireBound.Platform.Linux/    # Linux-specific implementations
├── WireBound.Platform.Stub/     # Stub/fallback implementations
└── WireBound.Avalonia/          # Cross-platform UI application
tests/
└── WireBound.Tests/             # Unit tests (TUnit + NSubstitute + AwesomeAssertions)
```

## Code Conventions

- **MVVM Pattern** with CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`)
- **Interface prefix**: `I` (e.g., `INetworkMonitorService`)
- **ViewModel suffix**: `ViewModel` (e.g., `OverviewViewModel`)
- **View suffix**: `View` or `Window` (e.g., `OverviewView`)
- **Platform prefix**: Platform name (e.g., `WindowsCpuInfoProvider`, `LinuxMemoryInfoProvider`)
- **Test suffix**: `Tests` (e.g., `OverviewViewModelTests`)
- **Commit format**: Conventional commits — `type(scope): description`
  - Examples: `feat(system): add CPU monitoring`, `fix(charts): correct Y-axis scaling`

## Adding New Features

1. Add models in `WireBound.Core/Models`
2. Add service interfaces in `WireBound.Core/Services`
3. For platform-specific features:
   - Add interface in `WireBound.Platform.Abstract/Services`
   - Implement in both `Platform.Windows` and `Platform.Linux`
   - Add stub in `Platform.Stub`
4. Register services in `App.axaml.cs` → `ConfigureServices()`
5. Create ViewModels inheriting `ObservableObject`
6. Create Views in `WireBound.Avalonia/Views`
7. Add tests in `WireBound.Tests`

## Pull Request Guidelines

- Keep changes focused and minimal
- Add tests for new functionality
- Ensure `dotnet build` and `dotnet run --project tests/WireBound.Tests/WireBound.Tests.csproj` pass
- Follow existing code conventions
- Update documentation if needed

## Known Gaps

See [docs/LIMITATIONS.md](docs/LIMITATIONS.md) for current limitations and planned features that could use help, particularly the elevated helper process for accurate per-connection byte tracking.
