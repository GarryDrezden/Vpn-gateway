<#
.SYNOPSIS
  Read-only driver / signing / boot-security diagnostics. Never changes settings.
#>
param(
    [string]$SysPath = ""
)

$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "_common.ps1")

$failed = $false
function Observe($outcome, $name, $msg) {
    Write-SvrResult $outcome $name $msg
    if ($outcome -eq "FAIL") { $script:failed = $true }
}

Observe $(if (Test-SvrElevated) { "PASS" } else { "WARNING" }) "elevation" $(if (Test-SvrElevated) { "elevated" } else { "not elevated (service/device checks may be incomplete)" })

$wdk = Test-SvrWdk
Observe $(if ($wdk.Ready) { "PASS" } else { "FAIL" }) "wdk" $(if ($wdk.Ready) { "WDK ready" } else { "missing: " + ($wdk.Missing -join "; ") })

if (-not $SysPath) { $SysPath = Get-SvrDefaultSysPath }
if ($SysPath -and (Test-Path $SysPath)) {
    Observe "PASS" "binary" $SysPath
    $sig = Get-AuthenticodeSignature $SysPath
    $sigOk = $sig.Status -eq "Valid"
    Observe $(if ($sigOk) { "PASS" } else { "WARNING" }) "signature" "$($sig.Status) $($sig.SignerCertificate.Subject)"
} else {
    Observe "FAIL" "binary" "SelectiveVpnCallout.sys not built"
}

$secureBoot = "unknown"
try {
    $sb = Get-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\SecureBoot\State" -Name UEFISecureBootEnabled -ErrorAction Stop
    $secureBoot = if ($sb.UEFISecureBootEnabled -eq 1) { "On (registry)" } else { "Off (registry)" }
} catch {
    $secureBoot = "registry unavailable (not UEFI or no permission)"
}
Observe "INFO" "secure-boot" $secureBoot

$testsigning = "unknown"
try {
    $enum = bcdedit /enum "{current}" 2>$null | Out-String
    if ($enum -match "testsigning\s+Yes") { $testsigning = "On" }
    elseif ($enum) { $testsigning = "Off" }
} catch { }
Observe "INFO" "testsigning" "$testsigning (read-only; this script will not change it)"

$hvci = "unknown"
try {
    $p = Get-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity" -ErrorAction Stop
    $hvci = if ($p.Enabled -eq 1) { "On" } else { "Off" }
} catch {
    $hvci = "registry not present / Off"
}
Observe "INFO" "hvci-memory-integrity" $hvci

$svc = Get-Service SelectiveVpnCallout -ErrorAction SilentlyContinue
if ($svc) {
    Observe $(if ($svc.Status -eq "Running") { "PASS" } else { "WARNING" }) "service" "$($svc.Status)"
} else {
    Observe "WARNING" "service" "not installed"
}

if (-not ("SvrDeviceCheck" -as [type])) {
    Add-Type @"
using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

public static class SvrDeviceCheck
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    public static bool TryOpenDevice(string path, out int lastError)
    {
        lastError = 0;
        using (SafeFileHandle handle = CreateFileW(
            path,
            GENERIC_READ | GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero,
            OPEN_EXISTING,
            0,
            IntPtr.Zero))
        {
            if (handle.IsInvalid)
            {
                lastError = Marshal.GetLastWin32Error();
                return false;
            }
            return true;
        }
    }
}
"@
}

try {
    $win32Error = 0
    $opened = [SvrDeviceCheck]::TryOpenDevice("\\.\SelectiveVpnCallout", [ref]$win32Error)
    if ($opened) {
        Observe "PASS" "device" "\\.\SelectiveVpnCallout opened"
    } else {
        $win32Msg = (New-Object System.ComponentModel.Win32Exception($win32Error)).Message
        Observe "FAIL" "device" "CreateFileW failed: Win32 $win32Error - $win32Msg"
    }
} catch {
    Observe "FAIL" "device" "CreateFileW helper error: $($_.Exception.GetType().Name): $($_.Exception.Message)"
}

Write-Host ""
Write-Host "This checker never enables TESTSIGNING, never disables Secure Boot/HVCI/BitLocker."
Write-Host "Manual signing workflow: docs/DRIVER_SIGNING.md"

if ($failed) { exit 1 }
exit 0
