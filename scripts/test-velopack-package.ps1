<#
.SYNOPSIS
    Validates that a Velopack output directory contains an installable release.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$OutputDirectory,

    [Parameter(Mandatory)]
    [ValidateSet("win-x64", "linux-x64")]
    [string]$Runtime,

    [Parameter(Mandatory)]
    [string]$Version
)

$ErrorActionPreference = "Stop"
$resolvedOutput = (Resolve-Path -LiteralPath $OutputDirectory).Path
$manifestPath = Join-Path $resolvedOutput "releases.$Runtime.json"

if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Velopack manifest is missing: $manifestPath"
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$assets = @($manifest.Assets)
$fullRelease = @($assets | Where-Object {
    "$($_.Version)" -eq $Version -and "$($_.FileName)" -like "*-full.nupkg"
})

if ($fullRelease.Count -ne 1) {
    throw "Expected exactly one full package for v$Version in $manifestPath; found $($fullRelease.Count)."
}

$packagePath = Join-Path $resolvedOutput $fullRelease[0].FileName
if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
    throw "The manifest references a missing package: $packagePath"
}

if ((Get-Item -LiteralPath $packagePath).Length -le 0) {
    throw "The full package is empty: $packagePath"
}

$expectedExecutable = if ($Runtime -eq "win-x64") { "WireBound.exe" } else { "WireBound" }
$expectedElevationExecutable = if ($Runtime -eq "win-x64") { "WireBound.Elevation.exe" } else { "wirebound-elevation" }
$archive = [System.IO.Compression.ZipFile]::OpenRead($packagePath)
try {
    $containsMainExecutable = $archive.Entries | Where-Object {
        [System.IO.Path]::GetFileName($_.FullName) -eq $expectedExecutable
    } | Select-Object -First 1

    if (-not $containsMainExecutable) {
        throw "The package does not contain $expectedExecutable."
    }

    $containsElevationExecutable = $archive.Entries | Where-Object {
        [System.IO.Path]::GetFileName($_.FullName) -eq $expectedElevationExecutable
    } | Select-Object -First 1

    if (-not $containsElevationExecutable) {
        throw "The package does not contain $expectedElevationExecutable."
    }
}
finally {
    $archive.Dispose()
}

$launcher = if ($Runtime -eq "win-x64") {
    Get-ChildItem -LiteralPath $resolvedOutput -Filter "*-Setup.exe" -File | Select-Object -First 1
}
else {
    Get-ChildItem -LiteralPath $resolvedOutput -Filter "*.AppImage" -File | Select-Object -First 1
}

if (-not $launcher -or $launcher.Length -le 0) {
    $launcherType = if ($Runtime -eq "win-x64") { "Windows Setup executable" } else { "Linux AppImage" }
    throw "The Velopack $launcherType is missing or empty in $resolvedOutput."
}

Write-Host "Validated Velopack $Runtime package v$Version"
Write-Host "  Manifest: $($manifestPath | Split-Path -Leaf)"
Write-Host "  Package:  $($packagePath | Split-Path -Leaf)"
Write-Host "  Launcher: $($launcher.Name)"
