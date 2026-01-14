<#
.SYNOPSIS
    Build and publish WireBound for distribution.

.DESCRIPTION
    This script builds WireBound in Release configuration and creates
    distributable packages for Windows, Linux, and macOS.

.PARAMETER Version
    The version number to use (e.g., "1.0.0"). If not specified, uses the version from the project file.

.PARAMETER OutputDir
    The output directory for published files. Defaults to "./publish".

.PARAMETER Runtime
    The target runtime identifier. Defaults to "win-x64".
    Supported: win-x64, linux-x64, osx-arm64, osx-x64

.PARAMETER SelfContained
    Whether to create a self-contained deployment. Defaults to $true.

.PARAMETER Clean
    Clean the output directory before publishing.

.EXAMPLE
    .\publish.ps1 -Version "1.0.0"

.EXAMPLE
    .\publish.ps1 -Version "1.0.0" -Runtime "linux-x64"

.EXAMPLE
    .\publish.ps1 -Clean -Runtime "osx-arm64"
#>

param(
    [string]$Version,
    [string]$OutputDir = "./publish",
    [ValidateSet("win-x64", "linux-x64", "osx-arm64", "osx-x64")]
    [string]$Runtime = "win-x64",
    [switch]$SelfContained = $true,
    [switch]$Clean
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
Write-Step "Building portable version for $Runtime..."

$portableOutput = Join-Path $OutputDir $Runtime

$publishArgs = @(
    "publish", $ProjectPath,
    "--configuration", "Release",
    "--runtime", $Runtime,
    "--output", $portableOutput,
    "-p:Version=$Version"
)

if ($SelfContained) {
    $publishArgs += "--self-contained", "true"
} else {
    $publishArgs += "--self-contained", "false"
}

& dotnet @publishArgs

if ($LASTEXITCODE -ne 0) { throw "Build failed" }

# Create archive
$targetIsWindows = $Runtime.StartsWith("win")
$archiveExt = if ($targetIsWindows) { "zip" } else { "tar.gz" }
$archivePath = Join-Path $OutputDir "WireBound-$Version-$Runtime.$archiveExt"

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
} else {
    Write-Host "📋 To install (macOS):"
    Write-Host "   1. Extract: tar -xzf $($archive.Name)"
    Write-Host "   2. Run: ./WireBound.Avalonia"
    Write-Host "   Note: You may need to allow the app in Security & Privacy settings"
}
Write-Host ""
