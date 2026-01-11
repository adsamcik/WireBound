<#
.SYNOPSIS
    Build and publish WireBound for distribution.

.DESCRIPTION
    This script builds WireBound in Release configuration and creates
    distributable packages for Windows.

.PARAMETER Version
    The version number to use (e.g., "1.0.0"). If not specified, uses the version from the project file.

.PARAMETER OutputDir
    The output directory for published files. Defaults to "./publish".

.PARAMETER PackageType
    The type of package to create: "Portable", "MSIX", or "Both". Defaults to "Portable".

.PARAMETER SelfContained
    Whether to create a self-contained deployment. Defaults to $true.

.PARAMETER Clean
    Clean the output directory before publishing.

.EXAMPLE
    .\publish.ps1 -Version "1.0.0" -PackageType "Portable"

.EXAMPLE
    .\publish.ps1 -PackageType "Both" -Clean
#>

param(
    [string]$Version,
    [string]$OutputDir = "./publish",
    [ValidateSet("Portable", "MSIX", "Both")]
    [string]$PackageType = "Portable",
    [switch]$SelfContained = $true,
    [switch]$Clean
)

$ErrorActionPreference = "Stop"
$ProjectPath = "$PSScriptRoot/../src/WireBound/WireBound.csproj"
$Runtime = "win-x64"

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
dotnet restore $ProjectPath
if ($LASTEXITCODE -ne 0) { throw "Restore failed" }
Write-Success "Dependencies restored"

# Build portable version
if ($PackageType -eq "Portable" -or $PackageType -eq "Both") {
    Write-Step "Building portable (unpackaged) version..."
    
    $portableOutput = Join-Path $OutputDir "portable"
    
    $publishArgs = @(
        "publish", $ProjectPath,
        "--configuration", "Release",
        "--runtime", $Runtime,
        "--output", $portableOutput,
        "-p:WindowsPackageType=None",
        "-p:PublishReadyToRun=true",
        "-p:Version=$Version"
    )
    
    if ($SelfContained) {
        $publishArgs += "--self-contained", "true"
    } else {
        $publishArgs += "--self-contained", "false"
    }
    
    & dotnet @publishArgs
    
    if ($LASTEXITCODE -ne 0) { throw "Portable build failed" }
    
    # Create zip archive
    $zipPath = Join-Path $OutputDir "WireBound-$Version-win-x64-portable.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path "$portableOutput\*" -DestinationPath $zipPath
    
    Write-Success "Portable version created: $zipPath"
}

# Build MSIX package
if ($PackageType -eq "MSIX" -or $PackageType -eq "Both") {
    Write-Step "Building MSIX package..."
    Write-Warning "MSIX packaging requires a valid certificate for signing"
    
    $msixOutput = Join-Path $OutputDir "msix"
    
    $publishArgs = @(
        "publish", $ProjectPath,
        "--configuration", "Release",
        "--runtime", $Runtime,
        "--output", $msixOutput,
        "-p:WindowsPackageType=MSIX",
        "-p:GenerateAppxPackageOnBuild=true",
        "-p:AppxPackageSigningEnabled=false",
        "-p:Version=$Version"
    )
    
    try {
        & dotnet @publishArgs
        
        if ($LASTEXITCODE -eq 0) {
            Write-Success "MSIX package created in: $msixOutput"
            Write-Warning "The MSIX is unsigned. To install, either:"
            Write-Host "  1. Sign it with a trusted certificate"
            Write-Host "  2. Enable Developer Mode on Windows"
            Write-Host "  3. Sideload with: Add-AppxPackage -Path <msix-file>"
        }
    } catch {
        Write-Warning "MSIX build failed: $_"
        Write-Host "This is expected if MSIX tooling is not fully configured."
    }
}

# Summary
Write-Host ""
Write-Host "╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║                    Build Complete!                           ║" -ForegroundColor Green
Write-Host "╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Green
Write-Host ""
Write-Host "Output files:"
Get-ChildItem $OutputDir -Recurse -File | ForEach-Object {
    $relativePath = $_.FullName.Replace($OutputDir, "").TrimStart("\")
    $size = "{0:N2} MB" -f ($_.Length / 1MB)
    Write-Host "  $relativePath ($size)"
}
Write-Host ""
