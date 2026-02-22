<#
.SYNOPSIS
    Build and publish WireBound for distribution.

.DESCRIPTION
    This script builds WireBound in Release configuration and creates
    distributable packages for Windows and Linux.

.PARAMETER Version
    The version number to use (e.g., "1.0.0"). If not specified, uses the version from the project file.

.PARAMETER OutputDir
    The output directory for published files. Defaults to "./publish".

.PARAMETER Runtime
    The target runtime identifier. Defaults to "win-x64".
    Supported: win-x64, linux-x64

.PARAMETER SelfContained
    Whether to create a self-contained deployment. Defaults to $true.

.PARAMETER Clean
    Clean the output directory before publishing.

.PARAMETER Aot
    Publish as Native AOT. Produces a single native binary with faster startup.
    AOT builds are always self-contained and require building on the target OS.

.EXAMPLE
    .\publish.ps1 -Version "1.0.0"

.EXAMPLE
    .\publish.ps1 -Version "1.0.0" -Runtime "linux-x64"

.EXAMPLE
    .\publish.ps1 -Version "1.0.0" -Aot

#>

param(
    [string]$Version,
    [string]$OutputDir = "./publish",
    [ValidateSet("win-x64", "linux-x64")]
    [string]$Runtime = "win-x64",
    [switch]$SelfContained = $true,
    [switch]$Clean,
    [switch]$Velopack,
    [switch]$Aot
)

$ErrorActionPreference = "Stop"
$ProjectPath = "$PSScriptRoot/../src/WireBound.Avalonia/WireBound.Avalonia.csproj"

# Colors for output
function Write-Step { param($Message) Write-Host "`n▶ $Message" -ForegroundColor Cyan }
function Write-Success { param($Message) Write-Host "✓ $Message" -ForegroundColor Green }
function Write-Warning { param($Message) Write-Host "⚠ $Message" -ForegroundColor Yellow }
function Write-Error { param($Message) Write-Host "✗ $Message" -ForegroundColor Red }

Write-Host ""
Write-Host "╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Magenta
Write-Host "║              WireBound Publishing Script                     ║" -ForegroundColor Magenta
Write-Host "╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Magenta

# Resolve paths
$ProjectPath = Resolve-Path $ProjectPath -ErrorAction Stop
$OutputDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDir)

Write-Host "`nProject: $ProjectPath"
Write-Host "Output:  $OutputDir"
Write-Host "Runtime: $Runtime"
if ($Aot) { Write-Host "Mode:    Native AOT" -ForegroundColor Yellow } else { Write-Host "Mode:    JIT (self-contained)" }

# Get version from project if not specified
if (-not $Version) {
    $csproj = [xml](Get-Content $ProjectPath)
    $Version = $csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if (-not $Version) { $Version = "1.0.0" }
}
Write-Host "Version: $Version"

# Clean output directory
if ($Clean -and (Test-Path $OutputDir)) {
    Write-Step "Cleaning output directory..."
    Remove-Item -Path $OutputDir -Recurse -Force
    Write-Success "Cleaned $OutputDir"
}

# Ensure output directory exists
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

# Restore dependencies
Write-Step "Restoring dependencies..."
$restoreArgs = @(
    "restore", $ProjectPath,
    "--runtime", $Runtime
)
& dotnet @restoreArgs
if ($LASTEXITCODE -ne 0) { throw "Restore failed" }
Write-Success "Dependencies restored"

# Build
$buildMode = if ($Aot) { "Native AOT" } else { "portable" }
Write-Step "Building $buildMode version for $Runtime..."

$portableOutput = Join-Path $OutputDir $Runtime

$publishArgs = @(
    "publish", $ProjectPath,
    "--configuration", "Release",
    "--runtime", $Runtime,
    "--output", $portableOutput,
    "-p:Version=$Version"
)

if ($Aot) {
    $publishArgs += "-p:PublishAot=true"
    # AOT is always self-contained
} elseif ($SelfContained) {
    $publishArgs += "--self-contained", "true"
} else {
    $publishArgs += "--self-contained", "false"
}

& dotnet @publishArgs

if ($LASTEXITCODE -ne 0) { throw "Build failed" }

# Publish Elevation Helper
$elevationProject = if ($Runtime.StartsWith("win")) { "WireBound.Elevation.Windows" } else { "WireBound.Elevation.Linux" }
Write-Step "Publishing $elevationProject for $Runtime..."
$ElevationProjectPath = Resolve-Path "$PSScriptRoot/../src/$elevationProject/$elevationProject.csproj" -ErrorAction Stop

$helperPublishArgs = @(
    "publish", $ElevationProjectPath,
    "--configuration", "Release",
    "--runtime", $Runtime,
    "--output", $portableOutput,
    "-p:Version=$Version"
)

# Elevation helpers always use AOT (PublishAot is in their csproj)
if ($SelfContained -or $Aot) {
    $helperPublishArgs += "--self-contained", "true"
} else {
    $helperPublishArgs += "--self-contained", "false"
}

& dotnet @helperPublishArgs

if ($LASTEXITCODE -ne 0) { throw "Elevation helper build failed" }
Write-Success "Elevation helper published"

# Create archive
$targetIsWindows = $Runtime.StartsWith("win")
$archiveExt = if ($targetIsWindows) { "zip" } else { "tar.gz" }
$aotSuffix = if ($Aot) { "-aot" } else { "" }
$archivePath = Join-Path $OutputDir "WireBound-$Version-$Runtime$aotSuffix.$archiveExt"

Write-Step "Creating archive..."

if ($targetIsWindows) {
    if (Test-Path $archivePath) { Remove-Item $archivePath -Force }
    Compress-Archive -Path "$portableOutput\*" -DestinationPath $archivePath
} else {
    # Use tar for Linux/macOS
    Push-Location $portableOutput
    try {
        if (Test-Path $archivePath) { Remove-Item $archivePath -Force }
        tar -czvf $archivePath .
    } finally {
        Pop-Location
    }
}

Write-Success "Archive created: $archivePath"

# Velopack packaging (optional)
if ($Velopack) {
    Write-Step "Creating Velopack package..."

    # Check for vpk CLI
    $vpkPath = Get-Command vpk -ErrorAction SilentlyContinue
    if (-not $vpkPath) {
        Write-Warning "vpk CLI not found. Install with: dotnet tool install -g vpk"
        Write-Warning "Skipping Velopack packaging"
    } else {
        $velopackOutput = Join-Path $OutputDir "velopack" $Runtime
        New-Item -ItemType Directory -Path $velopackOutput -Force | Out-Null

        $exeName = if ($targetIsWindows) { "WireBound.Avalonia.exe" } else { "WireBound.Avalonia" }

        $vpkArgs = @(
            "pack",
            "-u", "WireBound",
            "-v", $Version,
            "-p", $portableOutput,
            "-o", $velopackOutput,
            "-e", $exeName,
            "--channel", $Runtime
        )

        & vpk @vpkArgs

        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Velopack packaging failed (non-fatal)"
        } else {
            Write-Success "Velopack package created in $velopackOutput"
        }
    }
}

# Summary
Write-Host ""
Write-Host "╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║                    Build Complete!                           ║" -ForegroundColor Green
Write-Host "╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Green
Write-Host ""

# Get archive info
$archive = Get-Item $archivePath
$sizeMB = [math]::Round($archive.Length / 1MB, 2)

Write-Host "📦 Output: $archivePath"
Write-Host "   Size:   $sizeMB MB"
Write-Host ""

# Platform-specific instructions
if ($targetIsWindows) {
    Write-Host "📋 To install (Windows):"
    Write-Host "   1. Extract the ZIP file"
    Write-Host "   2. Run WireBound.Avalonia.exe"
} elseif ($Runtime.StartsWith("linux")) {
    Write-Host "📋 To install (Linux):"
    Write-Host "   1. Extract: tar -xzf $($archive.Name)"
    Write-Host "   2. Make executable: chmod +x WireBound.Avalonia"
    Write-Host "   3. Run: ./WireBound.Avalonia"
}
Write-Host ""
