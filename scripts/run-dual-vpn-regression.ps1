$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
& (Join-Path $Root "scripts/test-dual-vpn-preflight.ps1")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host ""
Write-Host "Manual steps before runtime checks:"
Write-Host "  1. Optional: leave external full-tunnel VPN as-is (not managed)."
Write-Host "  2. Connect Work VPN in UI (credentials + MFA)."
Write-Host "  3. Connect App VPN (selective)."
Write-Host "  4. Run diagnostic dual-vpn-coexistence from Diagnostics tab or IPC."
Write-Host ""
Write-Host "This runner does NOT reconnect Work VPN (avoids MFA loops)."