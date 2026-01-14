# WireBound - Network Traffic Monitor

A privacy-focused, cross-platform network traffic monitoring application built with .NET 10 and Avalonia UI.

## Features

- **Real-time Monitoring**: Track download/upload speeds with live updating charts
- **Session Statistics**: View data usage since app started
- **Historical Data**: Store and visualize daily/hourly network usage
- **Multiple Adapters**: Monitor individual network adapters or aggregate all
- **Privacy First**: All data stays local - no cloud, no telemetry
- **IP Helper API Fallback**: Robust monitoring even when Windows counters are broken

## Technology Stack

- **.NET 10** - Latest .NET framework
- **Avalonia UI** - Cross-platform UI framework (Windows, Linux, macOS)
- **SQLite** - Local database for historical data persistence
- **LiveCharts2** - Beautiful real-time charts
- **CommunityToolkit.Mvvm** - MVVM architecture support
- **Entity Framework Core** - Database ORM

## Project Structure

```
src/
├── WireBound.Core/           # Shared core library
│   ├── Data/                 # Database context and migrations
│   ├── Helpers/              # Utility classes
│   ├── Models/               # Domain models
│   └── Services/             # Service interfaces
│
└── WireBound.Avalonia/       # Cross-platform UI application
    ├── Services/             # Platform implementations
    ├── Styles/               # AXAML styles and themes
    ├── ViewModels/           # MVVM ViewModels
    └── Views/                # AXAML views
```

## Getting Started

### Prerequisites

- Windows 10/11, Linux, or macOS
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
```

### Debug in Visual Studio Code

1. Open the workspace in VS Code
2. Press `F5` to start debugging
3. Select ".NET Core Launch (console)" if prompted

## How It Works

### Network Monitoring (Method A - Default)

Uses `System.Net.NetworkInformation.NetworkInterface` to read:
- `BytesReceived` and `BytesSent` from IPv4 statistics
- Calculates speed by comparing values over time intervals

### IP Helper API (Method B - Fallback)

If Windows counters are corrupted, enable IP Helper API in settings:
- Uses P/Invoke to call `GetIfTable2` from `iphlpapi.dll`
- Talks directly to the kernel's network stack
- More robust but slightly higher resource usage

### Data Persistence

- Stats are saved to SQLite every 60 seconds (configurable)
- Hourly and daily aggregations for historical charts
- Data retention configurable (default: 365 days)

## Testing Your Setup

1. Launch the app and note the session download value
2. Download a known file (e.g., 100MB test file)
3. Verify the session download increased by ~100MB
4. If readings are incorrect, enable IP Helper API in Settings

## License

MIT License - Build your own, keep your data private!
