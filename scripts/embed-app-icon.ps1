param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$Ico
)
$ErrorActionPreference = "Stop"
if (-not (Test-Path $Exe)) { throw "Exe not found: $Exe" }
if (-not (Test-Path $Ico)) { throw "Ico not found: $Ico" }
$tools = Join-Path $PSScriptRoot "..\artifacts\tools"
$rcedit = Join-Path $tools "rcedit-x64.exe"
if (-not (Test-Path $rcedit)) {
    New-Item -ItemType Directory -Path $tools -Force | Out-Null
    Invoke-WebRequest -Uri "https://github.com/electron/rcedit/releases/download/v2.0.0/rcedit-x64.exe" -OutFile $rcedit -UseBasicParsing
}
& $rcedit $Exe --set-icon $Ico
if ($LASTEXITCODE -ne 0) { throw "rcedit failed with exit code $LASTEXITCODE" }
Write-Host "Applied icon to $Exe"