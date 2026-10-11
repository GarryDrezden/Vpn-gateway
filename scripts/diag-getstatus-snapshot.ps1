# Read-only one-shot VPN Route Service status snapshot (GetStatus via IPC).
# Does not mutate service, VPN, configuration, or registry. Writes JSON only under %TEMP%.
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "_common.ps1")
. (Join-Path $PSScriptRoot "_update-helpers.ps1")
$r = Invoke-SvrIpc -Method GetStatus -TimeoutMs 10000
if (-not $r.Ok) {
    Write-Output ("GetStatus failed: " + $r.Error)
    exit 1
}
$out = Join-Path $env:TEMP "vpnroute-diag-getstatus.json"
($r | ConvertTo-Json -Depth 8) | Set-Content -LiteralPath $out -Encoding UTF8
Write-Output ("Wrote diagnostic JSON to " + $out + " Ok=" + $r.Ok)
$p = $r.Payload
$vpn = $p.vpn
if ($vpn) {
    Write-Output ("OpenVPN Running=" + $vpn.running + " Connected=" + $vpn.connected + " Pid=" + $vpn.pid)
}
$bi = $p.browserIntegration
if ($bi) {
    Write-Output ("BrowserIntegration VpnEgress=" + $bi.vpnEgress.status + " ifIndex=" + $bi.vpnEgress.interfaceIndex + " name=" + $bi.vpnEgress.interfaceName)
    $ep = $bi.browserProxy.endpoint
    if ($ep) {
        Write-Output ("BrowserProxy=" + $bi.browserProxy.status + " " + $ep.host + ":" + $ep.port)
    }
}
$sel = $p.vpnAdapterSelection
if ($sel) {
    Write-Output ("VpnAdapterSelection ifIndex=" + $sel.ifIndex + " adapter=" + $sel.adapterName)
}
exit 0
