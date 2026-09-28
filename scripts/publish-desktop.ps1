# Publish portable desktop folder: App + Service + Probe (Release, win-x64, framework-dependent).

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "_common.ps1")

$root = Get-SvrRepoRoot
$out = Join-Path $root "artifacts\publish\SelectiveVpnRouter"
$ico = Join-Path $root "src\SelectiveVpnRouter.App\SelectiveVpnRouter.ico"
$embed = Join-Path $PSScriptRoot "embed-app-icon.ps1"

if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Path $out -Force | Out-Null

$publishArgs = @("publish", "-c", "Release", "-r", "win-x64", "--self-contained", "false", "-o", $out, "/p:PublishSingleFile=false")

Write-Host "Publishing SelectiveVpnRouter.App..."
dotnet @publishArgs (Join-Path $root "src\SelectiveVpnRouter.App\SelectiveVpnRouter.App.csproj") | Out-Host
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Publishing SelectiveVpnRouter.Service..."
dotnet @publishArgs (Join-Path $root "src\SelectiveVpnRouter.Service\SelectiveVpnRouter.Service.csproj") | Out-Host
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Publishing SelectiveVpnRouter.Probe..."
dotnet @publishArgs (Join-Path $root "src\SelectiveVpnRouter.Probe\SelectiveVpnRouter.Probe.csproj") | Out-Host
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$appExe = Join-Path $out "SelectiveVpnRouter.App.exe"
if ((Test-Path $embed) -and (Test-Path $ico) -and (Test-Path $appExe)) {
    Write-Host "Embedding app icon into publish exe..."
    & $embed -Exe $appExe -Ico $ico
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host "Published to: $out"