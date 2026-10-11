#Requires -Version 5.1
# Read-only regression entrypoint for Windows app icon pipeline (Phase 0).
$ErrorActionPreference = 'Stop'

$regression = Join-Path $PSScriptRoot '..\tests\scripts\app-icon-pipeline-regression.ps1'
if (-not (Test-Path -LiteralPath $regression)) {
    Write-Error "Missing regression script: $regression"
    exit 1
}

& $regression
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host 'PASS  app-icon-pipeline'
exit 0
