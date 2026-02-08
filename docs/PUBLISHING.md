# Publishing Guide

This document describes how to build and publish WireBound releases.

## Quick Start

### Local Build

```powershell
# Build portable for Windows (default)
.\scripts\publish.ps1 -Version "1.0.0"

# Build with clean output
.\scripts\publish.ps1 -Version "1.0.0" -Clean

# Build for Linux
.\scripts\publish.ps1 -Version "1.0.0" -Runtime linux-x64

# Build with Velopack installer (requires vpk CLI)
.\scripts\publish.ps1 -Version "1.0.0" -Velopack

# Install vpk CLI
dotnet tool install -g vpk
```

### Creating a Release

1. Update `CHANGELOG.md` with release notes
2. Create and push a version tag:
   ```bash
   git tag v1.0.0
   git push origin v1.0.0
   ```
3. GitHub Actions will automatically build and create a release for all platforms

## CI/CD Pipelines

### Workflows

| Workflow | Trigger | Purpose |
|----------|---------|---------|
| `ci.yml` | Push/PR to main | Build validation across Windows, Linux |
| `release.yml` | Version tags (`v*.*.*`) | Create GitHub releases with all platform builds |
| `nightly.yml` | Daily at 2 AM UTC | Automated nightly builds for all platforms |

### Manual Release

You can trigger a release manually:

1. Go to **Actions** → **Release** → **Run workflow**
2. Enter the version number (e.g., `1.0.0`)
3. Check "Is this a pre-release?" if applicable
4. Click **Run workflow**

## Build Outputs

### Distribution Formats

| Format | Platform | File | Auto-Update |
|--------|----------|------|-------------|
| Velopack Installer | Windows x64 | `WireBound-Setup.exe` | ✅ In-app |
| Velopack AppImage | Linux x64 | `WireBound.AppImage` | ✅ In-app |
| Portable ZIP | Windows x64 | `WireBound-<ver>-win-x64.zip` | ❌ Check-and-notify |
| Portable TAR.GZ | Linux x64 | `WireBound-<ver>-linux-x64.tar.gz` | ❌ Check-and-notify |

### Velopack Integration

WireBound uses [Velopack](https://velopack.io) for installed-mode auto-updates:

- **Delta updates**: Only changed files are downloaded
- **Automatic rollback**: If new version crashes on startup, reverts to previous
- **GitHub Releases**: Updates served directly from GitHub (no appcast needed)
- **Cross-platform**: Windows (Setup.exe) and Linux (AppImage)

Portable builds still receive update notifications but link to GitHub for manual download.

### Supported Platforms

| Platform | Runtime | Archive Format |
|----------|---------|----------------|
| Windows x64 | `win-x64` | `.zip` |
| Linux x64 | `linux-x64` | `.tar.gz` |

### Package Contents

- **Format:** Self-contained application bundle
- **Contents:** Executable with all dependencies (no .NET runtime required)
- **Executable:** `WireBound.Avalonia.exe` (Windows) or `WireBound.Avalonia` (Linux)

## Version Numbering

WireBound follows [Semantic Versioning](https://semver.org/):

- **MAJOR.MINOR.PATCH** (e.g., `1.2.3`)
- Major: Breaking changes
- Minor: New features (backwards compatible)
- Patch: Bug fixes

### Version Locations

Versions are set in:

1. `src/WireBound.Avalonia/WireBound.Avalonia.csproj`:
   - `<Version>` - NuGet/assembly version

## Installation Instructions

### Windows

1. Download `WireBound-<version>-win-x64.zip`
2. Extract to any folder
3. Run `WireBound.Avalonia.exe`

### Linux

```bash
# Download and extract
tar -xzf WireBound-<version>-linux-x64.tar.gz

# Make executable
chmod +x WireBound.Avalonia

# Run
./WireBound.Avalonia
```

## Troubleshooting

### Build Failures

**Problem:** .NET version mismatch
**Solution:** Ensure you have .NET 10 SDK installed. Check `global.json` for version requirements.

```powershell
dotnet --list-sdks
```

**Problem:** Restore fails for runtime
**Solution:** Ensure the runtime identifier is supported:

```powershell
dotnet restore --runtime win-x64
```

### Linux Issues

**Problem:** App won't start
**Solution:** Ensure execute permissions and required libraries:

```bash
chmod +x WireBound.Avalonia
# Install X11/Wayland dependencies if needed
sudo apt install libx11-6 libice6 libsm6
```

## Release Checklist

- [ ] Update version numbers in project files
- [ ] Update `CHANGELOG.md` with release notes
- [ ] Test the build locally with `scripts/publish.ps1`
- [ ] Commit changes and create version tag
- [ ] Verify GitHub Actions build succeeds for all platforms
- [ ] Test downloaded artifacts on each platform
- [ ] Publish release notes to GitHub Releases
