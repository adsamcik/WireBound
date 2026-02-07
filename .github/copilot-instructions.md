<!-- context-init:version:3.0.0 -->
<!-- context-init:generated:2026-02-07T14:21:00Z -->

# WireBound

<!-- context-init:managed -->
Cross-platform network traffic and system monitoring app. .NET 10, Avalonia UI, SQLite. Privacy-first — all data local.

## Tech Stack

<!-- context-init:managed -->
| Technology | Version | Purpose |
|-----------|---------|---------|
| .NET | 10.0 (SDK 10.0.102) | Runtime |
| Avalonia | 11.3.11 | Cross-platform UI |
| CommunityToolkit.Mvvm | 8.4.0 | MVVM source generators |
| EF Core SQLite | 10.0.2 | Local database |
| LiveChartsCore | 2.0.0-rc6.1 | Real-time charts |
| Serilog | 4.3.0 | Structured logging |
| TUnit | latest | Test framework |
| NSubstitute | latest | Mocking |
| AwesomeAssertions | latest | Fluent assertions |

## Project Layout

<!-- context-init:managed -->
| Project | Purpose |
|---------|---------|
| `WireBound.Avalonia` | UI app (Views, ViewModels, Services, Controls) |
| `WireBound.Core` | Shared library (Models, Interfaces, Helpers, DbContext) |
| `WireBound.Platform.Abstract` | Platform service interfaces |
| `WireBound.Platform.Windows` | Windows implementations |
| `WireBound.Platform.Linux` | Linux implementations |
| `WireBound.Platform.Stub` | Fallback/test implementations |
| `WireBound.Tests` | Unit tests |

## Code Style

<!-- context-init:managed -->
| Type | Convention | Example |
|------|-----------|---------|
| Files | PascalCase = class name | `NetworkAdapter.cs` |
| Interfaces | `I` prefix | `INetworkMonitorService` |
| ViewModels | `...ViewModel` | `OverviewViewModel` |
| Views | `...View` / `...Window` | `OverviewView` |
| Platform services | Platform prefix | `WindowsCpuInfoProvider` |
| Tests | `...Tests` | `OverviewViewModelTests` |
| Private fields | `_camelCase` | `_networkMonitor` |
| Async methods | `...Async` suffix | `SaveSettingsAsync()` |
| Constants | PascalCase | `Routes.Overview` |

## Patterns to Follow

<!-- context-init:managed -->
### MVVM (Source Generators)
```csharp
// Good: Use attributes for observable properties and commands
public partial class MyViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "";

    [RelayCommand]
    private async Task RefreshAsync() { }
}

// Bad: Manual INotifyPropertyChanged
public class MyViewModel : INotifyPropertyChanged { ... }
```

### Platform Abstraction
```csharp
// Good: Interface in Abstract, impl per platform, stub fallback
// Platform.Abstract/Services/IMyProvider.cs
public interface IMyProvider { Task<string> GetDataAsync(); }

// Platform.Stub/Services/StubMyProvider.cs
public sealed class StubMyProvider : IMyProvider { ... }

// Platform.Windows/Services/WindowsMyProvider.cs
[SupportedOSPlatform("windows")]
public sealed class WindowsMyProvider : IMyProvider { ... }
```

### DI Registration Order
```csharp
// Good: Stubs first (defaults), then platform overrides
StubPlatformServices.Instance.Register(services);
if (OperatingSystem.IsWindows())
    WindowsPlatformServices.Instance.Register(services);
```

### Interface Segregation
```csharp
// Good: Single impl, multiple interfaces
services.AddSingleton<DataPersistenceService>();
services.AddSingleton<IDataPersistenceService>(sp => sp.GetRequiredService<DataPersistenceService>());
services.AddSingleton<INetworkUsageRepository>(sp => sp.GetRequiredService<DataPersistenceService>());
```

## Testing

<!-- context-init:managed -->
- Framework: **TUnit** (use `[Test]`, NOT `[Fact]`)
- Mocking: **NSubstitute** (use `Substitute.For<T>()`, NOT `new Mock<T>()`)
- Assertions: **AwesomeAssertions** (`.Should().Be(...)`)
- Location: `tests/WireBound.Tests/`
- Run: `dotnet test`
- LiveCharts tests need `LiveChartsHook` assembly fixture

## Scripts

<!-- context-init:managed -->
- `dotnet restore` — restore dependencies
- `dotnet build` — build all projects
- `dotnet test` — run all tests
- `dotnet run --project src/WireBound.Avalonia/WireBound.Avalonia.csproj` — run app
- `.\scripts\publish.ps1 -Version "X.Y.Z"` — publish all platforms
- `.\scripts\publish.ps1 -Version "X.Y.Z" -Runtime win-x64` — publish specific platform

## Do NOT

<!-- context-init:managed -->
- Use xUnit attributes (`[Fact]`, `[Theory]`) — use TUnit (`[Test]`)
- Use Moq — use NSubstitute
- Use `DateTime.UtcNow` for user-facing timestamps — use `DateTime.Now` (local desktop app)
- Add package versions in `.csproj` — use `Directory.Packages.props`
- Use EF Core migrations CLI — use `ApplyMigrations()` in DbContext
- Skip stub implementations for new platform interfaces
- Update charts off the UI thread — use `Dispatcher.UIThread`

<!-- context-init:user-content-below -->
