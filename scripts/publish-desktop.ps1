# Publish portable desktop folder: App + Service + Probe (Release, win-x64, framework-dependent).

param(
    [string]$OutputDirectory = "",
    [switch]$Quiet,
    [switch]$NoRestore,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "_common.ps1")

if ($Quiet) { $global:SvrUpdateQuiet = $true }

$root = Get-SvrRepoRoot
$out = if ($OutputDirectory) { $OutputDirectory } else { Join-Path $root "artifacts\publish\SelectiveVpnRouter" }
$iconScript = Join-Path $PSScriptRoot "generate-app-icon.ps1"
if (Test-Path $iconScript) {
    & $iconScript -Quiet:$Quiet
}

$ico = Join-Path $root "assets\branding\vpn-route-icon.ico"
if (-not (Test-Path $ico)) {
    $ico = Join-Path $root "src\SelectiveVpnRouter.App\vpn-route-icon.ico"
}
$embed = Join-Path $PSScriptRoot "embed-app-icon.ps1"

if (Test-Path -LiteralPath $out) {
    if ($OutputDirectory) {
        Write-SvrUpdateDetail "Removing staging directory..."
        Remove-Item -LiteralPath $out -Recurse -Force -ErrorAction Stop
    }
    else {
        Remove-DirectoryWithRetry -Path $out -PublishDir $out
    }
}
New-Item -ItemType Directory -Path $out -Force | Out-Null

$publishArgs = @("publish", "-c", "Release", "-r", "win-x64", "--self-contained", "false", "-o", $out, "/p:PublishSingleFile=false")
if ($NoRestore) { $publishArgs += "--no-restore" }
if ($NoBuild) { $publishArgs += "--no-build" }

function Publish-Project {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [string]$Label
    )
    if (-not $Quiet -and -not $global:SvrUpdateLogPath) {
        Write-Host "Publishing $Label..."
        dotnet @($publishArgs + $ProjectPath)
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        return
    }
    Write-SvrUpdateDetail "Publishing $Label..."
    Invoke-SvrDotNet -ArgumentList ($publishArgs + $ProjectPath) -WorkingDirectory $root | Out-Null
}

Publish-Project -ProjectPath (Join-Path $root "src\SelectiveVpnRouter.App\SelectiveVpnRouter.App.csproj") -Label "SelectiveVpnRouter.App"
Publish-Project -ProjectPath (Join-Path $root "src\SelectiveVpnRouter.Service\SelectiveVpnRouter.Service.csproj") -Label "SelectiveVpnRouter.Service"
Publish-Project -ProjectPath (Join-Path $root "src\SelectiveVpnRouter.Probe\SelectiveVpnRouter.Probe.csproj") -Label "SelectiveVpnRouter.Probe"

$appExe = Join-Path $out "SelectiveVpnRouter.App.exe"
if ((Test-Path $embed) -and (Test-Path $ico) -and (Test-Path $appExe)) {
    Write-SvrUpdateDetail "Embedding app icon into publish exe..."
    & $embed -Exe $appExe -Ico $ico -Quiet:$Quiet 2>&1 | ForEach-Object { Write-SvrUpdateDetail "$_" }
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-SvrUpdateDetail "Published to: $out"
exit 0