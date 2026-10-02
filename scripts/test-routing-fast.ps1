#Requires -Version 5.1
param(
    [switch]$Quiet
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "_common.ps1")
if ($Quiet) { $global:SvrUpdateQuiet = $true }

function Write-RoutingFastDetail {
    param([Parameter(Mandatory = $true)][string]$Line)
    Write-SvrUpdateLogLine $Line
    if (-not $Quiet) {
        Write-Host $Line
    }
}

function Write-RoutingFastLine {
    param(
        [Parameter(Mandatory = $true)][ValidateSet("PASS", "FAIL", "SKIP")][string]$Outcome,
        [string]$Reason = ""
    )
    if ($Quiet) { return }
    switch ($Outcome) {
        "PASS" { Write-Host "PASS  routing-fast" -ForegroundColor Green }
        "SKIP" { Write-Host "SKIP  routing-fast: $Reason" -ForegroundColor DarkGray }
        default { Write-Host "FAIL  routing-fast: $Reason" -ForegroundColor Red }
    }
}

function Invoke-RoutingDiagnostic {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [int]$TimeoutMs = 120000
    )

    $payload = (@{ name = $Name } | ConvertTo-Json -Compress)
    $resp = Invoke-SvrIpc -Method "RunDiagnostic" -PayloadJson $payload -TimeoutMs $TimeoutMs
    if (-not $resp.Ok) {
        throw "RunDiagnostic $Name failed: $($resp.Error)"
    }

    $result = $resp.PayloadJson | ConvertFrom-Json
    Write-RoutingFastDetail "routing-fast $Name => $($result.Outcome): $($result.Message)"
    if ($result.Outcome -ne "PASS") {
        throw "routing-fast diagnostic '$Name' returned $($result.Outcome)"
    }
}

try {
    $statusResp = Invoke-SvrIpcGetStatusWithRetry -MaxAttempts 24 -PollIntervalMs 250 -TimeoutMs 15000

    $snap = $statusResp.PayloadJson | ConvertFrom-Json
    if (-not $snap.VpnRoutingReady) {
        Write-RoutingFastDetail "routing-fast: SKIP (VPN disconnected)"
        Write-RoutingFastLine -Outcome SKIP -Reason "VPN disconnected"
        exit 2
    }

    Invoke-RoutingDiagnostic -Name "callout-registration" -TimeoutMs 30000
    Invoke-RoutingDiagnostic -Name "wfp-app-filters" -TimeoutMs 30000
    Invoke-RoutingDiagnostic -Name "transparent-routing" -TimeoutMs 120000

    Write-RoutingFastDetail "routing-fast: PASS"
    Write-RoutingFastLine -Outcome PASS
    exit 0
}
catch {
    $reason = $_.Exception.Message
    Write-RoutingFastDetail "routing-fast: FAIL $reason"
    Write-RoutingFastLine -Outcome FAIL -Reason $reason
    exit 1
}