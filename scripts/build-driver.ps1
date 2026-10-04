<#
.SYNOPSIS
  Build SelectiveVpnCallout.sys into isolated staging (never the registered/live runtime path).
#>
param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",
    [ValidateSet("x64")]
    [string]$Platform = "x64",
    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "_common.ps1")

$repoRoot = Get-SvrRepoRoot
if (-not $OutputDirectory) {
    $OutputDirectory = Get-SvrDriverStagingSysPath -Root $repoRoot -Configuration $Configuration
    $OutputDirectory = Split-Path -Parent $OutputDirectory
}
$outDir = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $outDir.EndsWith([IO.Path]::DirectorySeparatorChar)) { $outDir += [IO.Path]::DirectorySeparatorChar }
$intDir = Join-Path $repoRoot "artifacts\driver\obj\staging\$Configuration"
New-Item -ItemType Directory -Force -Path $outDir, $intDir | Out-Null

$registered = (Get-ItemProperty -LiteralPath "HKLM:\SYSTEM\CurrentControlSet\Services\SelectiveVpnCallout" -Name ImagePath -ErrorAction SilentlyContinue).ImagePath
$expectedSys = Join-Path $outDir.TrimEnd('\') "SelectiveVpnCallout.sys"
if ($registered) {
    $regPath = $registered.Trim().Trim('"')
    if ($regPath.StartsWith('\??\')) { $regPath = $regPath.Substring(4) }
    try { $regPath = [IO.Path]::GetFullPath($regPath) } catch {}
    if ($regPath -and ($regPath.Equals($expectedSys, [StringComparison]::OrdinalIgnoreCase))) {
        Write-SvrResult "FAIL" "build-driver" "Refusing to link to registered ImagePath: $regPath"
        exit 1
    }
}

$wdk = Test-SvrWdk
if (-not $wdk.Ready) {
    Write-SvrResult 'FAIL' 'build-driver' 'WDK is not available.'
    exit 1
}

$msbuild = Get-SvrMsbuild
if (-not $msbuild) {
    Write-SvrResult "FAIL" "msbuild" "MSBuild.exe not found"
    exit 1
}

$proj = Join-Path $repoRoot "driver\SelectiveVpnCallout\SelectiveVpnCallout.vcxproj"
Write-SvrResult "INFO" "build-driver" "OutDir=$outDir IntDir=$intDir"
& $msbuild $proj /p:Configuration=$Configuration /p:Platform=$Platform /p:OutDir="$outDir" /p:IntDir="$intDir\" /m
if ($LASTEXITCODE -ne 0) {
    Write-SvrResult "FAIL" "build-driver" "MSBuild exited $LASTEXITCODE"
    exit $LASTEXITCODE
}

$sys = Join-Path $outDir.TrimEnd('\') "SelectiveVpnCallout.sys"
if (-not (Test-Path -LiteralPath $sys)) {
    Write-SvrResult "FAIL" "build-driver" "MSBuild succeeded but $sys is missing"
    exit 1
}

Write-SvrResult "PASS" "build-driver" $sys
exit 0