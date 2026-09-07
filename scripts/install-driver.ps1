<#
.SYNOPSIS
  Install and start SelectiveVpnCallout.sys as a demand-start kernel service.
  Does NOT enable TESTSIGNING, Secure Boot changes, BitLocker, or HVCI.
#>
param(
    [string]$SysPath = ""
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "_common.ps1")

if (-not (Test-SvrElevated)) {
    Write-SvrResult "FAIL" "elevation" "Re-run from an elevated PowerShell."
    exit 1
}

if (-not $SysPath) {
    $SysPath = Get-SvrDefaultSysPath
}
if (-not $SysPath -or -not (Test-Path $SysPath)) {
    Write-SvrResult "FAIL" "binary" "SelectiveVpnCallout.sys not found. Run scripts\build-driver.ps1 first."
    exit 1
}

$SysPath = [IO.Path]::GetFullPath($SysPath)
$sig = Get-AuthenticodeSignature $SysPath
Write-SvrResult "INFO" "signature" "$($sig.Status) $($sig.SignerCertificate.Subject)"

$testsigning = $false
try {
    $out = bcdedit /enum "{current}" 2>$null | Out-String
    $testsigning = $out -match "testsigning\s+Yes"
} catch { }

if ($sig.Status -ne "Valid" -and -not $testsigning) {
    Write-SvrResult "FAIL" "install-driver" "Binary is not trusted (Authenticode=$($sig.Status)) and TESTSIGNING is off. See docs/DRIVER_SIGNING.md. This script will not enable test signing."
    exit 1
}

if ($sig.Status -ne "Valid" -and $testsigning) {
    Write-SvrResult "WARNING" "signature" "Unsigned/test binary allowed only because TESTSIGNING is already on (set manually)."
}

sc.exe stop SelectiveVpnCallout 2>$null | Out-Null
Start-Sleep -Milliseconds 400
sc.exe delete SelectiveVpnCallout 2>$null | Out-Null
$create = sc.exe create SelectiveVpnCallout type= kernel start= demand binPath= "$SysPath" DisplayName= "Selective VPN Router WFP callout"
Write-Host $create
sc.exe start SelectiveVpnCallout
if ($LASTEXITCODE -ne 0) {
    Write-SvrResult "FAIL" "start" "sc start failed. If code 577/577-equivalent: signature policy blocked the driver. See docs/DRIVER_SIGNING.md."
    exit 1
}

Start-Sleep -Milliseconds 300
$svc = Get-Service SelectiveVpnCallout -ErrorAction SilentlyContinue
if ($svc.Status -ne "Running") {
    Write-SvrResult "FAIL" "install-driver" "Service not running ($($svc.Status))"
    exit 1
}

Write-SvrResult "PASS" "install-driver" "Loaded from $SysPath"
exit 0
