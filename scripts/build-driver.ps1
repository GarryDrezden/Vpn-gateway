<#
.SYNOPSIS
  Build SelectiveVpnCallout.sys (Release|x64 by default).
  Never enables TESTSIGNING / Secure Boot / HVCI changes.
#>
param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",
    [ValidateSet("x64")]
    [string]$Platform = "x64"
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "_common.ps1")

$wdk = Test-SvrWdk
Write-SvrResult "INFO" "vs" $(if ($wdk.VsInstall) { $wdk.VsInstall } else { "not found" })
Write-SvrResult $(if ($wdk.Toolset) { "PASS" } else { "FAIL" }) "wdk-toolset" $(if ($wdk.Toolset) { "WindowsKernelModeDriver10.0 present" } else { "missing" })
Write-SvrResult $(if ($wdk.FwpskHeader) { "PASS" } else { "FAIL" }) "fwpsk.h" $(if ($wdk.FwpskPath) { $wdk.FwpskPath } else { "kernel WFP headers not installed" })
Write-SvrResult $(if ($wdk.WdfHeader) { "PASS" } else { "FAIL" }) "wdf.h" $(if ($wdk.WdfHeader) { "KMDF headers present" } else { "KMDF headers not installed" })

if (-not $wdk.Ready) {
    Write-SvrResult 'FAIL' 'build-driver' 'WDK is not available. Driver binary was NOT built. Source exists but is not an implemented loaded driver.'
    Write-Host ''
    Write-Host 'Install, then re-run this script:'
    Write-Host '  1. Visual Studio 2022 - workload Desktop development with C++'
    Write-Host '  2. Windows SDK 10.0.22621 or newer (user-mode SDK is not enough)'
    Write-Host '  3. Windows Driver Kit matching that SDK:'
    Write-Host '     https://learn.microsoft.com/en-us/windows-hardware/drivers/download-the-wdk'
    Write-Host '     VS Installer: Windows Driver Kit / WDK VSIX'
    Write-Host '  Required after WDK setup:'
    Write-Host '     - MSBuild platform toolset WindowsKernelModeDriver10.0'
    Write-Host '     - Windows Kits\10\Include\<ver>\km\fwpsk.h'
    Write-Host '     - Windows Kits\10\Include\wdf\kmdf\<ver>\wdf.h'
    Write-Host 'This script does not change Secure Boot, BitLocker, Memory Integrity, or TESTSIGNING.'
    exit 1
}

$msbuild = Get-SvrMsbuild
if (-not $msbuild) {
    Write-SvrResult "FAIL" "msbuild" "MSBuild.exe not found"
    exit 1
}

$proj = Join-Path (Get-SvrRepoRoot) "driver\SelectiveVpnCallout\SelectiveVpnCallout.vcxproj"
Write-SvrResult "INFO" "msbuild" $msbuild
& $msbuild $proj /p:Configuration=$Configuration /p:Platform=$Platform /m
if ($LASTEXITCODE -ne 0) {
    Write-SvrResult "FAIL" "build-driver" "MSBuild exited $LASTEXITCODE"
    exit $LASTEXITCODE
}

$sys = Join-Path (Get-SvrRepoRoot) "artifacts\driver\$Configuration\SelectiveVpnCallout.sys"
if (-not (Test-Path $sys)) {
    Write-SvrResult "FAIL" "build-driver" "MSBuild succeeded but $sys is missing"
    exit 1
}

Write-SvrResult "PASS" "build-driver" $sys
exit 0
