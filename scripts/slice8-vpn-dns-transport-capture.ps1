# Slice 8 - read-only VPN-bound UDP DNS transport capture (production SocketInterfaceBinder + wire format).
# Does NOT deploy, restart Service, toggle VPN, or change routes/DNS/adapters.
#Requires -Version 5.1
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "_common.ps1")

$RepoRoot = Get-SvrRepoRoot
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$outDir = Join-Path $RepoRoot "artifacts\diagnostics"
$DiagProbeDir = Join-Path $outDir "probe"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
New-Item -ItemType Directory -Force -Path $DiagProbeDir | Out-Null
$outFile = Join-Path $outDir "slice8-vpn-dns-transport-$stamp.txt"

$lines = New-Object System.Collections.Generic.List[string]
function Write-Capture {
    param([string]$Line)
    $lines.Add($Line)
    Write-Host $Line
}

function Write-CaptureAndExit {
    param(
        [int]$ExitCode,
        [string]$Reason
    )
    if ($Reason) {
        Write-Capture $Reason
    }
    $lines | Set-Content -LiteralPath $outFile -Encoding UTF8
    Write-Host ("Wrote " + $outFile)
    exit $ExitCode
}

# Acceptance diagnostic inputs for this profile (NOT used in production code).
$AcceptanceDnsServers = @("1.1.1.1", "1.0.0.1", "9.9.9.10", "149.112.112.10")
$DiagHostname = "api.ipify.org"
$TimeoutMs = 5000

function Get-DiagnosticProbePath {
    return (Join-Path $DiagProbeDir "SelectiveVpnRouter.Probe.exe")
}

function Build-DiagnosticProbe {
    Write-Capture "Building current Release diagnostic Probe (artifacts/diagnostics/probe)..."
    $proj = Join-Path $RepoRoot "src\SelectiveVpnRouter.Probe\SelectiveVpnRouter.Probe.csproj"
    $null = & dotnet build $proj -c Release -o $DiagProbeDir --nologo -v q 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build diagnostic Probe failed"
    }
    $path = Get-DiagnosticProbePath
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Diagnostic Probe missing after build: " + $path
    }
    Write-Output -NoEnumerate $path
}

function Get-ProbeOutputText {
    param([object[]]$Out)
    if ($null -eq $Out) { return "" }
    return (($Out | ForEach-Object { "$_" }) -join [Environment]::NewLine)
}

function Test-LegacyProbeFallbackOutput {
    param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return $false }
    if ($Text -notmatch '(?m)^process ') { return $false }
    if ($Text -match '(?m)^finalResolutionStatus=') { return $false }
    if ($Text -match '(?m)^bindOk=') { return $false }
    return $true
}

function Test-UdpDiagOutputSchema {
    param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return $false }
    if (Test-LegacyProbeFallbackOutput -Text $Text) { return $false }
    $required = @(
        '(?m)^server=',
        '(?m)^bindOk=',
        '(?m)^receiveOutcome=',
        '(?m)^finalResolutionStatus='
    )
    foreach ($pattern in $required) {
        if ($Text -notmatch $pattern) { return $false }
    }
    return $true
}

function Test-ResolverDiagOutputSchema {
    param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return $false }
    if (Test-LegacyProbeFallbackOutput -Text $Text) { return $false }
    $required = @(
        '(?m)^resolverStatus=',
        '(?m)^resolverElapsedMs=',
        '(?m)^sessionDnsOrder='
    )
    foreach ($pattern in $required) {
        if ($Text -notmatch $pattern) { return $false }
    }
    return $true
}

function Invoke-DiagnosticProbeCapabilityCheck {
    param([string]$ProbePath)
    Write-Capture "--- Diagnostic Probe capability self-check (pre-gate) ---"
    Write-Capture ("ProbePath=" + $ProbePath)

    $udpOut = & $ProbePath --diag-vpn-dns-udp --if-index 1 --hostname example.com --server 1.1.1.1 --timeout-ms 100 2>&1
    $udpText = Get-ProbeOutputText -Out @($udpOut)
    Write-Capture "CapabilityCheckUdpCommand=--diag-vpn-dns-udp --if-index 1 --hostname example.com --server 1.1.1.1 --timeout-ms 100"
    Write-Capture ("CapabilityCheckUdpExitCode=" + $LASTEXITCODE)
    foreach ($line in @($udpOut)) { Write-Capture $line }

    if (-not (Test-UdpDiagOutputSchema -Text $udpText)) {
        Write-Capture "DIAGNOSTIC PROBE INVALID"
        Write-Capture "Reason: --diag-vpn-dns-udp did not emit required schema (bindOk/receiveOutcome/finalResolutionStatus)."
        Write-Capture "Hint: published/service Probe may predate diagnostic flags; this script uses artifacts/diagnostics/probe only."
        return $false
    }

    $resOut = & $ProbePath --diag-vpn-dns-resolver --if-index 1 --hostname example.com --servers 1.1.1.1 --timeout-ms 100 2>&1
    $resText = Get-ProbeOutputText -Out @($resOut)
    Write-Capture "CapabilityCheckResolverCommand=--diag-vpn-dns-resolver --if-index 1 --hostname example.com --servers 1.1.1.1 --timeout-ms 100"
    Write-Capture ("CapabilityCheckResolverExitCode=" + $LASTEXITCODE)
    foreach ($line in @($resOut)) { Write-Capture $line }

    if (-not (Test-ResolverDiagOutputSchema -Text $resText)) {
        Write-Capture "DIAGNOSTIC PROBE INVALID"
        Write-Capture "Reason: --diag-vpn-dns-resolver did not emit required schema (resolverStatus/resolverElapsedMs/sessionDnsOrder)."
        return $false
    }

    Write-Capture "DiagnosticProbeCapability=PASS"
    return $true
}

Write-Capture "=== Slice 8 VPN-bound UDP DNS transport capture ==="
Write-Capture ("StartedUtc=" + [DateTimeOffset]::UtcNow.ToString("o"))
Write-Capture ("StartedLocal=" + (Get-Date -Format "o"))
Write-Capture ("RepoRoot=" + $RepoRoot)
Write-Capture ("Elevated=" + (Test-SvrElevated))
Write-Capture ("DiagHostname=" + $DiagHostname)
Write-Capture ("TimeoutMsPerServer=" + $TimeoutMs)
Write-Capture ("Note: Uses current diagnostic Probe build; never artifacts/publish Probe for --diag-vpn-dns-*")

try {
    $probe = (Build-DiagnosticProbe | Select-Object -Last 1)
}
catch {
    Write-CaptureAndExit -ExitCode 3 -Reason ("DIAGNOSTIC PROBE INVALID`nReason: " + $_.Exception.Message)
}

if (-not (Invoke-DiagnosticProbeCapabilityCheck -ProbePath $probe)) {
    Write-CaptureAndExit -ExitCode 3 -Reason ""
}

$ipc = Invoke-SvrIpc -Method GetStatus -TimeoutMs 15000
if (-not $ipc.Ok) {
    Write-Capture "CONNECTED CAPTURE INVALID"
    Write-Capture ("GetStatus failed: " + $ipc.Error)
    Write-CaptureAndExit -ExitCode 2 -Reason ""
}

$p = $ipc.PayloadJson | ConvertFrom-Json
$vpn = $p.vpn
$bi = $p.browserIntegration

Write-Capture "--- Service gate ---"
Write-Capture ("vpn.running=" + $vpn.running)
Write-Capture ("vpn.connected=" + $vpn.connected)
Write-Capture ("browserIntegration.vpnEgress.status=" + $bi.vpnEgress.status)
Write-Capture ("browserIntegration.vpnEgress.interfaceIndex=" + $(if ($null -ne $bi.vpnEgress.interfaceIndex) { $bi.vpnEgress.interfaceIndex } else { "" }))
Write-Capture ("browserIntegration.vpnEgress.interfaceName=" + $(if ($bi.vpnEgress.interfaceName) { $bi.vpnEgress.interfaceName } else { "" }))

$gateOk = ($vpn.running -eq $true) -and ($vpn.connected -eq $true) -and ($bi.vpnEgress.status -eq "Ready")
if (-not $gateOk) {
    Write-Capture "CONNECTED CAPTURE INVALID"
    Write-Capture "Reason: vpn.running, vpn.connected, and vpnEgress.status=Ready are required."
    Write-CaptureAndExit -ExitCode 1 -Reason ""
}

$ifIndex = [int]$bi.vpnEgress.interfaceIndex
if ($ifIndex -le 0) {
    Write-Capture "CONNECTED CAPTURE INVALID"
    Write-Capture "Reason: vpnEgress.interfaceIndex missing despite Ready."
    Write-CaptureAndExit -ExitCode 1 -Reason ""
}

Write-Capture ""
Write-Capture "--- Route/IP comparison (read-only) ---"
try {
    Get-NetIPAddress -InterfaceIndex $ifIndex -ErrorAction Stop | Format-Table -AutoSize | Out-String | ForEach-Object { Write-Capture $_.TrimEnd() }
}
catch { Write-Capture ("Get-NetIPAddress: " + $_.Exception.Message) }
try {
    Get-NetRoute -InterfaceIndex $ifIndex -ErrorAction Stop | Select-Object -First 15 | Format-Table -AutoSize | Out-String | ForEach-Object { Write-Capture $_.TrimEnd() }
}
catch { Write-Capture ("Get-NetRoute: " + $_.Exception.Message) }

Write-Capture ""
Write-Capture "--- Static resolver note (for ~5033ms live SOCKS) ---"
Write-Capture "VpnInterfaceDnsResolver: DefaultTimeoutMs=5000 per server attempt."
Write-Capture "Transport timeout returns Outcome=Cancelled; resolver currently returns Timeout immediately (does NOT try next server)."
Write-Capture "OperationCanceledException path continues; Cancelled outcome does not - see static bug note in report."

Write-Capture ""
Write-Capture "--- Per-server production bound UDP DNS probes ---"

$summary = @()
$toolingFailed = $false
foreach ($server in $AcceptanceDnsServers) {
    Write-Capture ""
    Write-Capture ("===== server " + $server + " =====")
    $cmd = @(
        $probe,
        "--diag-vpn-dns-udp",
        "--if-index", $ifIndex,
        "--hostname", $DiagHostname,
        "--server", $server,
        "--timeout-ms", $TimeoutMs
    )
    Write-Capture ("Command=" + ($cmd -join " "))
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $out = & $probe --diag-vpn-dns-udp --if-index $ifIndex --hostname $DiagHostname --server $server --timeout-ms $TimeoutMs 2>&1
    $sw.Stop()
    $outText = Get-ProbeOutputText -Out @($out)
    Write-Capture ("ExitCode=" + $LASTEXITCODE)
    Write-Capture ("WallClockMs=" + $sw.ElapsedMilliseconds)
    foreach ($line in @($out)) { Write-Capture $line }

    if (-not (Test-UdpDiagOutputSchema -Text $outText)) {
        Write-Capture "DIAGNOSTIC PROBE INVALID"
        Write-Capture ("Reason: missing UDP diagnostic schema for server " + $server)
        $toolingFailed = $true
        break
    }

    $final = ($outText | Select-String -Pattern '(?m)^finalResolutionStatus=(.+)$' | ForEach-Object { $_.Matches[0].Groups[1].Value.Trim() }) | Select-Object -First 1
    $recv = ($outText | Select-String -Pattern '(?m)^receiveOutcome=(\w+)' | ForEach-Object { $_.Matches[0].Groups[1].Value }) | Select-Object -First 1
    $bind = ($outText | Select-String -Pattern '(?m)^bindOk=(True|False)' | ForEach-Object { $_.Matches[0].Groups[1].Value }) | Select-Object -First 1
    $summary += [pscustomobject]@{
        Server = $server
        ExitCode = $LASTEXITCODE
        BindOk = $bind
        ReceiveOutcome = $recv
        Final = $final
        WallMs = $sw.ElapsedMilliseconds
    }
}

if ($toolingFailed) {
    Write-CaptureAndExit -ExitCode 3 -Reason ""
}

Write-Capture ""
Write-Capture "--- Full production resolver (session DNS order, same transport) ---"
$resolverServers = $AcceptanceDnsServers -join ","
Write-Capture ("Command=" + $probe + " --diag-vpn-dns-resolver --if-index " + $ifIndex + " --hostname " + $DiagHostname + " --servers " + $resolverServers + " --timeout-ms " + $TimeoutMs)
$rsw = [System.Diagnostics.Stopwatch]::StartNew()
$resOut = & $probe --diag-vpn-dns-resolver --if-index $ifIndex --hostname $DiagHostname --servers $resolverServers --timeout-ms $TimeoutMs 2>&1
$rsw.Stop()
$resText = Get-ProbeOutputText -Out @($resOut)
Write-Capture ("ResolverExitCode=" + $LASTEXITCODE)
Write-Capture ("ResolverWallClockMs=" + $rsw.ElapsedMilliseconds)
foreach ($line in @($resOut)) { Write-Capture $line }

if (-not (Test-ResolverDiagOutputSchema -Text $resText)) {
    Write-Capture "DIAGNOSTIC PROBE INVALID"
    Write-Capture "Reason: missing resolver diagnostic schema."
    Write-CaptureAndExit -ExitCode 3 -Reason ""
}

Write-Capture ""
Write-Capture "--- Summary ---"
$summary | Format-Table -AutoSize | Out-String | ForEach-Object { Write-Capture $_.TrimEnd() }

$timeouts = @($summary | Where-Object { $_.Final -eq "Timeout" })
$successes = @($summary | Where-Object { $_.Final -eq "Success" })
if ($summary.Count -gt 0 -and $successes.Count -eq $summary.Count) {
    Write-Capture "PRIMARY_CLASS=ALL_DNS_SUCCESS (all bound UDP probes returned Success)"
}
elseif ($summary.Count -eq $timeouts.Count) {
    Write-Capture "PRIMARY_CLASS=A (all bound UDP queries TIMEOUT - bind/routing/provider UDP/53)"
}
elseif ($successes.Count -gt 0) {
    Write-Capture "PRIMARY_CLASS=B_or_partial (some DNS servers respond - check resolver retry/order)"
}
else {
    Write-Capture "PRIMARY_CLASS=G (mixed - inspect per-server finalResolutionStatus above)"
}

Write-Capture ""
Write-Capture "=== End capture ==="
$lines | Set-Content -LiteralPath $outFile -Encoding UTF8
Write-Host ""
Write-Host ("Wrote " + $outFile)
exit 0
