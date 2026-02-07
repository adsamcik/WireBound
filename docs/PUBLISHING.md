# Publishing Guide

This document describes how to build and publish WireBound releases.

## Quick Start

### Local Build

```powershell
# Build for Windows (default)
.\scripts\publish.ps1 -Version "1.0.0"

# Build with clean output
.\scripts\publish.ps1 -Version "1.0.0" -Clean

# Build for Linux
.\scripts\publish.ps1 -Version "1.0.0" -Runtime linux-x64
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
