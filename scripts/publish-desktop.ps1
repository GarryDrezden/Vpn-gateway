# Publish dev desktop runtime folder: App + Service + Probe + Bootstrap + driver + manifest.

param(
    [string]$OutputDirectory = "",
    [switch]$Quiet,
    [switch]$NoRestore,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "_common.ps1")
. (Join-Path $PSScriptRoot "branding-asset-paths.ps1")

if ($Quiet) { $global:SvrUpdateQuiet = $true }

$root = Get-SvrRepoRoot
$branding = Get-SvrBrandingAssetPaths -RepoRoot $root
$out = if ($OutputDirectory) { $OutputDirectory } else { Join-Path $root "artifacts\publish\SelectiveVpnRouter" }
$iconScript = Join-Path $PSScriptRoot "generate-app-icon.ps1"
if (Test-Path -LiteralPath $iconScript) {
    & $iconScript -SourcePng $branding.SourcePng -OutputIco $branding.GeneratedIco -PreviewPng $branding.PreviewPng -SkipIfUpToDate -Quiet:$Quiet
}

$ico = $branding.GeneratedIco
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
Publish-Project -ProjectPath (Join-Path $root "src\SelectiveVpnRouter.Bootstrap\SelectiveVpnRouter.Bootstrap.csproj") -Label "SelectiveVpnRouter.Bootstrap"

. (Join-Path $PSScriptRoot "portable-package-files.ps1")
Ensure-SvrPortableDriverLayout -LayoutRoot $out -RepoRoot $root
Write-SvrPortableManifestForLayout -LayoutRoot $out -RepoRoot $root

$appExe = Join-Path $out "SelectiveVpnRouter.App.exe"
$publishedSidecar = Join-Path $out "vpn-route-icon.ico"
if ((Test-Path -LiteralPath $embed) -and (Test-Path -LiteralPath $ico) -and (Test-Path -LiteralPath $appExe)) {
    Write-SvrUpdateDetail "Embedding app icon into publish exe..."
    & $embed -Exe $appExe -Ico $ico -Quiet:$Quiet 2>&1 | ForEach-Object { Write-SvrUpdateDetail "$_" }
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
if ((Test-Path -LiteralPath $ico) -and -not (Test-Path -LiteralPath $publishedSidecar)) {
    Copy-Item -LiteralPath $ico -Destination $publishedSidecar -Force
}

Write-SvrUpdateDetail "Published to: $out"
exit 0
