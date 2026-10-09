# Slice 8 — one-shot read-only capture while VPN Route tunnel is CONNECTED and vpnEgress Ready.
# Does not stop/restart Service, connect/disconnect VPN, alter routes/DNS/adapters, or deploy.
#Requires -Version 5.1
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "_common.ps1")
. (Join-Path $PSScriptRoot "_update-helpers.ps1")

$RepoRoot = Get-SvrRepoRoot
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$outDir = Join-Path $RepoRoot "artifacts\diagnostics"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$outFile = Join-Path $outDir "slice8-connected-dns-$stamp.txt"

$lines = New-Object System.Collections.Generic.List[string]
function Write-Capture {
    param([string]$Line)
    $lines.Add($Line)
    Write-Host $Line
}

function Resolve-ProbeExe {
    $candidates = @(
        (Join-Path $RepoRoot "artifacts\publish\SelectiveVpnRouter\SelectiveVpnRouter.Probe.exe"),
        (Join-Path $RepoRoot "artifacts\portable\VPN-Route-0.2.0-x64\SelectiveVpnRouter.Probe.exe"),
        (Join-Path $RepoRoot "artifacts\installer-staging\SelectiveVpnRouter.Probe.exe")
    )
    foreach ($path in $candidates) {
        if (Test-Path -LiteralPath $path) { return $path }
    }
    try {
        $svc = Get-CimInstance Win32_Service -Filter "Name='SelectiveVpnRouter'" -ErrorAction SilentlyContinue
        if ($svc -and $svc.PathName) {
            $svcDir = Split-Path -Parent ($svc.PathName.Trim('"').Split(" ")[0])
            $nextToService = Join-Path $svcDir "SelectiveVpnRouter.Probe.exe"
            if (Test-Path -LiteralPath $nextToService) { return $nextToService }
        }
    }
    catch { }
    return $null
}

function Initialize-NetworkInterfaceDnsSnapshot {
    if (-not ("Slice8NetDnsSnapshot" -as [type])) {
        $csharp = @'
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

public static class Slice8NetDnsSnapshot
{
    public static string DescribeInterface(int interfaceIndex)
    {
        var sb = new StringBuilder();
        NetworkInterface match = null;
        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            try
            {
                if (nic.GetIPProperties().GetIPv4Properties().Index != interfaceIndex)
                    continue;
                match = nic;
                break;
            }
            catch (NetworkInformationException) { }
        }

        if (match == null)
        {
            sb.AppendLine("No NetworkInterface with IPv4 ifIndex=" + interfaceIndex);
            return sb.ToString();
        }

        sb.AppendLine("Name=" + match.Name);
        sb.AppendLine("Description=" + match.Description);
        sb.AppendLine("OperationalStatus=" + match.OperationalStatus);
        sb.AppendLine("IPv4InterfaceIndex=" + match.GetIPProperties().GetIPv4Properties().Index);
        sb.AppendLine("DnsAddresses:");
        foreach (IPAddress dns in match.GetIPProperties().DnsAddresses)
        {
            sb.AppendLine("  " + dns + " family=" + dns.AddressFamily);
        }

        sb.AppendLine("DeployedGetIpv4DnsServers(requires OperationalStatus.Up)=");
        sb.AppendLine("  " + string.Join(",", GetIpv4DnsServersDeployed(interfaceIndex)));
        sb.AppendLine("IfIndexOnlyGetIpv4DnsServers(no Up filter)=");
        sb.AppendLine("  " + string.Join(",", GetIpv4DnsServersByIndexOnly(interfaceIndex)));
        return sb.ToString();
    }

    // Mirrors deployed Service semantics (pre speculative Up-filter removal).
    public static IList<string> GetIpv4DnsServersDeployed(int interfaceIndex)
    {
        var list = new List<string>();
        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;
            try
            {
                IPInterfaceProperties props = nic.GetIPProperties();
                if (props.GetIPv4Properties().Index != interfaceIndex)
                    continue;
                foreach (IPAddress dns in props.DnsAddresses)
                {
                    if (dns.AddressFamily == AddressFamily.InterNetwork)
                        list.Add(dns.ToString());
                }
                return list;
            }
            catch (NetworkInformationException) { }
        }
        return list;
    }

    public static IList<string> GetIpv4DnsServersByIndexOnly(int interfaceIndex)
    {
        var list = new List<string>();
        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            try
            {
                IPInterfaceProperties props = nic.GetIPProperties();
                if (props.GetIPv4Properties().Index != interfaceIndex)
                    continue;
                foreach (IPAddress dns in props.DnsAddresses)
                {
                    if (dns.AddressFamily == AddressFamily.InterNetwork)
                        list.Add(dns.ToString());
                }
                return list;
            }
            catch (NetworkInformationException) { }
        }
        return list;
    }
}
'@
        Add-Type -TypeDefinition $csharp -Language CSharp -ErrorAction Stop | Out-Null
    }
}

Write-Capture "=== Slice 8 connected DNS / SOCKS capture ==="
Write-Capture ("StartedUtc=" + [DateTimeOffset]::UtcNow.ToString("o"))
Write-Capture ("StartedLocal=" + (Get-Date -Format "o"))
Write-Capture ("RepoRoot=" + $RepoRoot)
Write-Capture ("Elevated=" + (Test-SvrElevated))

$svc = Get-Service -Name "SelectiveVpnRouter" -ErrorAction SilentlyContinue
Write-Capture ("ServiceStatus=" + $(if ($svc) { $svc.Status } else { "NOT_FOUND" }))

$ipc = Invoke-SvrIpc -Method GetStatus -TimeoutMs 15000
if (-not $ipc.Ok) {
    Write-Capture "CONNECTED CAPTURE INVALID"
    Write-Capture ("GetStatus failed: " + $ipc.Error)
    $lines | Set-Content -LiteralPath $outFile -Encoding UTF8
    Write-Host ""
    Write-Host ("Wrote " + $outFile)
    exit 2
}

if ([string]::IsNullOrWhiteSpace($ipc.PayloadJson)) {
    Write-Capture "CONNECTED CAPTURE INVALID"
    Write-Capture "GetStatus returned empty PayloadJson."
    $lines | Set-Content -LiteralPath $outFile -Encoding UTF8
    Write-Host ""
    Write-Host ("Wrote " + $outFile)
    exit 2
}

$p = $ipc.PayloadJson | ConvertFrom-Json
$vpn = $p.vpn
$bi = $p.browserIntegration

Write-Capture "--- Authoritative Service state ---"
Write-Capture ("vpn.running=" + $vpn.running)
Write-Capture ("vpn.connected=" + $vpn.connected)
Write-Capture ("vpn.pid=" + $(if ($null -ne $vpn.pid) { $vpn.pid } else { "" }))
Write-Capture ("browserIntegration.vpnEgress.status=" + $bi.vpnEgress.status)
Write-Capture ("browserIntegration.vpnEgress.interfaceIndex=" + $(if ($null -ne $bi.vpnEgress.interfaceIndex) { $bi.vpnEgress.interfaceIndex } else { "" }))
Write-Capture ("browserIntegration.vpnEgress.interfaceName=" + $(if ($bi.vpnEgress.interfaceName) { $bi.vpnEgress.interfaceName } else { "" }))
Write-Capture ("browserIntegration.browserProxy.status=" + $bi.browserProxy.status)
$ep = $bi.browserProxy.endpoint
$proxyHost = $null
$proxyPort = $null
if ($ep) {
    $proxyHost = $ep.host
    $proxyPort = $ep.port
    Write-Capture ("browserIntegration.browserProxy.endpoint=" + $proxyHost + ":" + $proxyPort)
}
Write-Capture ("browserIntegration.ruleCount=" + $bi.ruleCount)
Write-Capture ("integrationApiVersion=" + $bi.integrationApiVersion)

$gateOk = ($vpn.connected -eq $true) -and ($bi.vpnEgress.status -eq "Ready")
if (-not $gateOk) {
    Write-Capture ""
    Write-Capture "CONNECTED CAPTURE INVALID"
    Write-Capture "Reason: vpn.connected must be true AND browserIntegration.vpnEgress.status must be Ready."
    $lines | Set-Content -LiteralPath $outFile -Encoding UTF8
    Write-Host ""
    Write-Host ("Wrote " + $outFile)
    exit 1
}

$ifIndex = [int]$bi.vpnEgress.interfaceIndex
if ($ifIndex -le 0) {
    Write-Capture "CONNECTED CAPTURE INVALID"
    Write-Capture "Reason: vpnEgress.interfaceIndex missing despite Ready."
    $lines | Set-Content -LiteralPath $outFile -Encoding UTF8
    Write-Host ""
    Write-Host ("Wrote " + $outFile)
    exit 1
}

Write-Capture ""
Write-Capture "--- Windows adapter snapshot (ifIndex=$ifIndex) ---"
try {
    Get-NetAdapter -InterfaceIndex $ifIndex -ErrorAction Stop | Format-List * | Out-String | ForEach-Object { Write-Capture $_.TrimEnd() }
}
catch {
    Write-Capture ("Get-NetAdapter: " + $_.Exception.Message)
}
try {
    Get-NetIPInterface -InterfaceIndex $ifIndex -ErrorAction Stop | Format-List * | Out-String | ForEach-Object { Write-Capture $_.TrimEnd() }
}
catch {
    Write-Capture ("Get-NetIPInterface: " + $_.Exception.Message)
}
try {
    Get-NetIPAddress -InterfaceIndex $ifIndex -ErrorAction Stop | Format-List * | Out-String | ForEach-Object { Write-Capture $_.TrimEnd() }
}
catch {
    Write-Capture ("Get-NetIPAddress: " + $_.Exception.Message)
}
try {
    Get-DnsClientServerAddress -InterfaceIndex $ifIndex -ErrorAction Stop | Format-List * | Out-String | ForEach-Object { Write-Capture $_.TrimEnd() }
}
catch {
    Write-Capture ("Get-DnsClientServerAddress: " + $_.Exception.Message)
}
try {
    Get-NetRoute -InterfaceIndex $ifIndex -ErrorAction Stop | Format-Table -AutoSize | Out-String | ForEach-Object { Write-Capture $_.TrimEnd() }
}
catch {
    Write-Capture ("Get-NetRoute: " + $_.Exception.Message)
}

Write-Capture ""
Write-Capture "--- NetworkInterface (.NET) snapshot ---"
Write-Capture "Note: DeployedGetIpv4DnsServers uses OperationalStatus.Up (currently deployed Service semantics)."
Initialize-NetworkInterfaceDnsSnapshot
Write-Capture ([Slice8NetDnsSnapshot]::DescribeInterface($ifIndex).TrimEnd())

Write-Capture ""
Write-Capture "--- Classification hints ---"
$winV4 = @()
try {
    $winV4 = @(Get-DnsClientServerAddress -InterfaceIndex $ifIndex -AddressFamily IPv4 -ErrorAction Stop | ForEach-Object { $_.ServerAddresses } | Where-Object { $_ })
}
catch { }
$deployed = [Slice8NetDnsSnapshot]::GetIpv4DnsServersDeployed($ifIndex)
$byIndex = [Slice8NetDnsSnapshot]::GetIpv4DnsServersByIndexOnly($ifIndex)
Write-Capture ("Windows_GetDnsClientServerAddress_IPv4=" + ($(if ($winV4.Count) { $winV4 -join "," } else { "(none)" })))
Write-Capture ("DotNet_DeployedLookup_IPv4=" + ($(if ($deployed.Count) { ($deployed | ForEach-Object { $_ }) -join "," } else { "(none)" })))
Write-Capture ("DotNet_IfIndexOnlyLookup_IPv4=" + ($(if ($byIndex.Count) { ($byIndex | ForEach-Object { $_ }) -join "," } else { "(none)" })))

if (-not $proxyHost -or -not $proxyPort) {
    Write-Capture ""
    Write-Capture "CONNECTED CAPTURE INVALID"
    Write-Capture "Reason: browserProxy endpoint missing."
    $lines | Set-Content -LiteralPath $outFile -Encoding UTF8
    Write-Host ""
    Write-Host ("Wrote " + $outFile)
    exit 1
}

$probe = Resolve-ProbeExe
Write-Capture ""
Write-Capture "--- SOCKS probe (Probe.exe --via-proxy) ---"
if (-not $probe) {
    Write-Capture "Probe.exe not found (skipped)."
}
else {
    Write-Capture ("ProbePath=" + $probe)
    $via = $proxyHost + ":" + $proxyPort
    Write-Capture ("Command=" + $probe + " --via-proxy " + $via + " --http https://api.ipify.org")
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $probeOut = & $probe --via-proxy $via --http "https://api.ipify.org" 2>&1
    $sw.Stop()
    Write-Capture ("DurationMs=" + $sw.ElapsedMilliseconds)
    Write-Capture ("ExitCode=" + $LASTEXITCODE)
    foreach ($line in @($probeOut)) {
        Write-Capture $line
    }
    $joined = ($probeOut | Out-String)
    if ($joined -match "status=(\d+)") {
        Write-Capture ("ParsedSocksRep=0x{0:X2} ({1})" -f [int]$Matches[1], $Matches[1])
    }
    elseif ($joined -match "http OK") {
        Write-Capture "ParsedSocksRep=0x00 (inferred from http OK)"
    }
}

Write-Capture ""
Write-Capture "=== End capture ==="
$lines | Set-Content -LiteralPath $outFile -Encoding UTF8
Write-Host ""
Write-Host ("Wrote " + $outFile)
exit 0
