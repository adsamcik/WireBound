# Publishing Guide

This document describes how to build and publish WireBound releases.

## Quick Start

### Local Build

```powershell
# Build portable version
.\scripts\publish.ps1 -Version "1.0.0" -PackageType Portable

# Build with clean output
.\scripts\publish.ps1 -Version "1.0.0" -PackageType Portable -Clean

# Build both portable and MSIX
.\scripts\publish.ps1 -Version "1.0.0" -PackageType Both
```

### Creating a Release

1. Update `CHANGELOG.md` with release notes
2. Create and push a version tag:
   ```bash
   git tag v1.0.0
   git push origin v1.0.0
   ```
3. GitHub Actions will automatically build and create a release

## CI/CD Pipelines

### Workflows

| Workflow | Trigger | Purpose |
|----------|---------|---------|
| `ci.yml` | Push/PR to main | Build validation & code quality |
| `release.yml` | Version tags (`v*.*.*`) | Create GitHub releases |
| `nightly.yml` | Daily at 2 AM UTC | Automated nightly builds |

### Manual Release

You can trigger a release manually:

1. Go to **Actions** → **Release** → **Run workflow**
2. Enter the version number (e.g., `1.0.0`)
3. Check "Is this a pre-release?" if applicable
4. Click **Run workflow**

## Build Outputs

### Portable (Unpackaged)

- **Format:** ZIP archive
- **Contents:** Self-contained executable with all dependencies
- **Requirements:** Windows 10 19041+
- **Installation:** Extract and run `WireBound.exe`

### MSIX Package

- **Format:** Windows app package
- **Requirements:** Code signing certificate for trusted installation
- **Installation:** Double-click or use `Add-AppxPackage`

## Version Numbering

WireBound follows [Semantic Versioning](https://semver.org/):

- **MAJOR.MINOR.PATCH** (e.g., `1.2.3`)
- Major: Breaking changes
- Minor: New features (backwards compatible)
- Patch: Bug fixes

### Version Locations

Versions are set in these locations:

1. `src/WireBound/WireBound.csproj`:
   - `<Version>` - NuGet/assembly version
   - `<ApplicationDisplayVersion>` - Display version
   - `<ApplicationVersion>` - Build number

2. `src/WireBound/Platforms/Windows/Package.appxmanifest`:
   - `<Identity Version="...">` - MSIX package version

## Code Signing

### For MSIX Distribution

To distribute MSIX packages outside the Microsoft Store, you need a code signing certificate:

1. **Development/Testing:**
   - Enable Developer Mode in Windows Settings
   - Use self-signed certificate

2. **Production:**
   - Obtain a certificate from a trusted CA
   - Store certificate in GitHub Secrets:
     - `WINDOWS_SIGNING_CERT` - Base64-encoded .pfx
     - `WINDOWS_SIGNING_PASSWORD` - Certificate password

### Self-Signed Certificate (Development)

```powershell
# Create self-signed certificate
$cert = New-SelfSignedCertificate `
    -Type Custom `
    -Subject "CN=WireBound Dev" `
    -KeyUsage DigitalSignature `
    -FriendlyName "WireBound Development" `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")

# Export certificate
$password = ConvertTo-SecureString -String "YourPassword" -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath "WireBound-Dev.pfx" -Password $password
```

## Troubleshooting

### Build Failures

**Problem:** MAUI workload not found
```
error NETSDK1147: To build this project, the following workloads must be installed: maui-windows
```
**Solution:**
```powershell
dotnet workload install maui-windows
```

**Problem:** .NET version mismatch
**Solution:** Ensure you have .NET 10 SDK installed. Check `global.json` for version requirements.

### MSIX Issues

**Problem:** Package unsigned
**Solution:** For development, enable Developer Mode. For production, use a valid code signing certificate.

**Problem:** MSIX won't install
**Solution:** Ensure the target machine trusts the certificate or has Developer Mode enabled.

## Release Checklist

- [ ] Update version numbers in project files
- [ ] Update `CHANGELOG.md` with release notes
- [ ] Test the build locally with `scripts/publish.ps1`
- [ ] Commit changes and create version tag
- [ ] Verify GitHub Actions build succeeds
- [ ] Test downloaded artifacts on clean machine
- [ ] Publish release notes to GitHub Releases
