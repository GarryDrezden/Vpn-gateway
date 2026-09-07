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

$deviceOk = $false
try {
    $fs = [IO.File]::Open("\\.\SelectiveVpnCallout", [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::ReadWrite)
    $fs.Close()
    $deviceOk = $true
} catch { }
Observe $(if ($deviceOk) { "PASS" } else { "FAIL" }) "device" $(if ($deviceOk) { "\\.\SelectiveVpnCallout opened" } else { "device not openable (driver not loaded or ACL)" })

Write-Host ""
Write-Host "This checker never enables TESTSIGNING, never disables Secure Boot/HVCI/BitLocker."
Write-Host "Manual signing workflow: docs/DRIVER_SIGNING.md"

if ($failed) { exit 1 }
exit 0
