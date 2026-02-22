# WireBound - Network & System Monitor

A privacy-focused, cross-platform network traffic and system monitoring application built with .NET 10 and Avalonia UI.

## Features

- **Real-time Monitoring**: Track download/upload speeds with live updating charts
- **System Monitoring**: CPU and memory usage tracking with circular gauges and sparklines
- **Session & Historical Statistics**: View current session data and historical daily/hourly aggregations
- **Multiple Adapters**: Monitor individual network adapters or aggregate all (WiFi, Ethernet, VPN detection)
- **Per-Application Network Tracking**: See which applications are using your network with connection enumeration
- **Privacy First**: All data stays local - no cloud, no telemetry
- **Cross-Platform**: Runs on Windows and Linux with platform-specific implementations

## Technology Stack

- **.NET 10** - Latest .NET framework
- **Avalonia UI** - Cross-platform UI framework (Windows, Linux)
- **SQLite** - Local database for historical data persistence
- **LiveCharts2** - Beautiful real-time charts
- **CommunityToolkit.Mvvm** - MVVM architecture support
- **Entity Framework Core** - Database ORM
- **Platform Abstraction Layer** - Separate projects for Windows and Linux with a shared interface layer and development stubs

## Project Structure

```
src/
├── WireBound.Core/              # Shared core library
│   ├── Data/                    # Database context
│   ├── Helpers/                 # Utility classes
│   ├── Models/                  # Domain models
│   └── Services/                # Service interfaces
│
├── WireBound.Platform.Abstract/ # Platform abstraction layer
│   ├── Models/                  # Platform-specific models
│   └── Services/                # Platform service interfaces
│
├── WireBound.Platform.Windows/  # Windows implementations
│   └── Services/                # Windows services (WiFi, CPU, Memory, Process)
│
├── WireBound.Platform.Linux/    # Linux implementations
│   └── Services/                # Linux services (WiFi, CPU, Memory, Process)
│
├── WireBound.Platform.Stub/     # Stub/fallback implementations
│   └── Services/                # Development stubs
│
└── WireBound.Avalonia/          # Cross-platform UI application
    ├── Controls/                # Custom controls
    ├── Converters/              # XAML value converters
    ├── Helpers/                 # UI helpers
    ├── Services/                # Application services
    ├── Styles/                  # AXAML styles and themes
    ├── ViewModels/              # MVVM ViewModels
    └── Views/                   # AXAML views
```

## Getting Started

### Prerequisites

- Windows 10/11 or Linux
- .NET 10 SDK
- Visual Studio 2022 or VS Code with C# extension

### Build and Run

```bash
# Restore dependencies
dotnet restore

# Build the project
dotnet build

# Run the application
dotnet run --project src/WireBound.Avalonia/WireBound.Avalonia.csproj

# Run tests
dotnet run --project tests/WireBound.Tests/WireBound.Tests.csproj
```

### Debug in Visual Studio Code

1. Open the workspace in VS Code
2. Press `F5` to start debugging
3. Select ".NET Core Launch (console)" if prompted

## How It Works

### Network Monitoring

Uses `System.Net.NetworkInformation.NetworkInterface` to read:
- `BytesReceived` and `BytesSent` from IPv4 statistics
- Calculates speed by comparing values over time intervals

### Data Persistence

- Stats are saved to SQLite every 60 seconds (configurable)
- Hourly and daily aggregations for historical charts
- Data retention configurable (default: 365 days)

## Testing Your Setup

1. Launch the app and note the session download value
2. Download a known file (e.g., 100MB test file)
3. Verify the session download increased by ~100MB

## Known Limitations

See [docs/LIMITATIONS.md](docs/LIMITATIONS.md) for detailed information on:
- Per-process byte tracking (requires elevated helper, currently uses estimates)
- Platform-specific feature availability

## Native AOT Builds

WireBound supports Native AOT compilation for faster startup, smaller binaries, and no JIT overhead.

### Building with AOT

```bash
# Publish with AOT (must build on the target OS — cannot cross-compile)
.\scripts\publish.ps1 -Version "1.0.0" -Runtime win-x64 -Aot
.\scripts\publish.ps1 -Version "1.0.0" -Runtime linux-x64 -Aot

# Or directly with dotnet CLI
dotnet publish src/WireBound.Avalonia/WireBound.Avalonia.csproj -c Release -r win-x64 --self-contained -p:PublishAot=true
```

### AOT vs JIT Comparison

| Aspect | AOT | JIT (default) |
|--------|-----|---------------|
| Startup time | ~instant | ~1-2s |
| Binary size | ~47 MB + native DLLs | ~150+ MB (self-contained) |
| Cross-compile | ❌ Must build on target OS | ✅ Can cross-compile |
| XAML previewer | ❌ Not in Release builds | ✅ Works normally |

### Known Limitations

- **Cross-compilation**: AOT builds must be compiled on the target OS (build Windows on Windows, Linux on Linux)
- **XAML previewer**: Does not work in AOT Release builds; use Debug/JIT builds for design work
- **Native DLLs**: SkiaSharp, HarfBuzz, SQLite, and OpenGL DLLs are distributed alongside the AOT binary
- **EF Core**: Uses runtime model building with warning suppressions; EF Core compiled models can be adopted when AOT support stabilizes

### Elevation Helpers

The elevation helper processes (`WireBound.Elevation.Windows`, `WireBound.Elevation.Linux`) are always compiled with AOT, producing compact ~7 MB native binaries.

## Roadmap

- [ ] Elevated helper process for accurate per-app byte tracking
- [ ] Light/Dark theme toggle
- [ ] Auto-update notifications
- [ ] Responsive layout for various window sizes
- [ ] Package manager distribution (choco, apt)

## License

MIT License - Build your own, keep your data private!
