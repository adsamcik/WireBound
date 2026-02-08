<#
.SYNOPSIS
    Runs WireBound tests in a Linux Docker container.
.DESCRIPTION
    Builds and runs the test suite in a Linux container to exercise
    platform-specific code paths (/proc, Unix sockets, SO_PEERCRED).
.PARAMETER Filter
    Optional TUnit filter expression to run specific tests.
.EXAMPLE
    .\scripts\run-linux-tests.ps1
    .\scripts\run-linux-tests.ps1 -Filter "LinuxIntegration"
#>
param(
    [string]$Filter
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent

Write-Host "Building Linux test container..." -ForegroundColor Cyan
docker build -t wirebound-linux-tests -f "$repoRoot\tests\Dockerfile.linux-tests" "$repoRoot"
if ($LASTEXITCODE -ne 0) { throw "Docker build failed" }

$dockerArgs = @()
if ($Filter) {
    $dockerArgs += "--treenode-filter", $Filter
}

Write-Host "Running tests in Linux container..." -ForegroundColor Cyan
docker run --rm wirebound-linux-tests @dockerArgs
$exitCode = $LASTEXITCODE

if ($exitCode -eq 0) {
    Write-Host "All tests passed!" -ForegroundColor Green
} else {
    Write-Host "Tests failed with exit code $exitCode" -ForegroundColor Red
}

exit $exitCode
