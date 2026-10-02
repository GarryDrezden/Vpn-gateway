$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$scriptsRoot = $PSScriptRoot
. (Join-Path $scriptsRoot "_common.ps1")
. (Join-Path $scriptsRoot "_update-helpers.ps1")
. (Join-Path $scriptsRoot "_runtime-diagnostic-report-state.ps1")
. (Join-Path $scriptsRoot "_runtime-diagnostic-report.ps1")
. (Join-Path $scriptsRoot "_runtime-diagnostic-mock-helpers.ps1")
. (Join-Path $scriptsRoot "_vpn-lifecycle-acceptance.ps1")

function Assert-True { param([bool]$C, [string]$M) if (-not $C) { throw $M } }
function Assert-Equal { param($E, $A, [string]$M) if ($E -ne $A) { throw "$M expected=$E actual=$A" } }

function Initialize-VpnLifecycleMockHost {
    param([string[]]$DiagnosticNames = (Get-VpnLifecycleAcceptanceDiagnosticNames))

    $repoRoot = Get-SvrRepoRoot
    $reportPath = Join-Path $env:TEMP ("svr-lifecycle-seq-" + [Guid]::NewGuid().ToString("N") + ".txt")
    Initialize-SvrRuntimeDiagnosticReportState -StartedAt (Get-Date) -RepoRoot $repoRoot -ReportPath $reportPath
    Set-Variable -Scope Script -Name IpcReadinessSeconds -Value 5
    Set-Variable -Scope Script -Name WaitConnectedSeconds -Value 1
    Set-Variable -Scope Script -Name DiagnosticNames -Value $DiagnosticNames
    Set-Variable -Scope Script -Name PollIntervalMs -Value 50
    Set-Variable -Scope Script -Name StatusTimeoutMs -Value 5000
    Set-Variable -Scope Script -Name IpcAttemptTimeoutMs -Value 5000
    Set-Variable -Scope Script -Name DiagnosticTimeoutMs -Value 5000
    Set-Variable -Scope Script -Name DiagnosticServicePollMs -Value 50

    function script:Add-Line {
        param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Line)
        Add-SvrRuntimeDiagnosticLine -Line $Line
    }
    function script:Flush-Report { param([int]$FinalExitCode = 3) Flush-SvrRuntimeDiagnosticReport -FinalExitCode $FinalExitCode }
    function script:Save-Report { param([Parameter(Mandatory = $true)][int]$FinalExitCode) Save-SvrRuntimeDiagnosticReport -FinalExitCode $FinalExitCode }

    return $reportPath
}

function Register-VpnLifecycleSequenceMock {
    param(
        [hashtable]$OutcomeByDiagnostic = @{}
    )

    $script:VpnLifecycleMockOutcomeByDiagnostic = @{}
    foreach ($key in $OutcomeByDiagnostic.Keys) {
        $script:VpnLifecycleMockOutcomeByDiagnostic[$key] = $OutcomeByDiagnostic[$key]
    }

    $script:SvrServiceProcessIdOverride = { return 4242 }

    $script:SvrIpcInvokeOverride = {
        param(
            [Parameter(Mandatory = $true)][string]$Method,
            [string]$PayloadJson,
            [int]$TimeoutMs = 15000,
            [int]$WallClockTimeoutMs = 0
        )

        if ($Method -ne 'GetStatus') {
            throw "Unexpected Invoke-SvrIpc method in lifecycle mock: $Method"
        }

        return New-SvrSparseIpcEnvelope -Ok $true -PayloadJson (New-SvrMockGetStatusPayload -VpnRoutingReady $true)
    }

    $script:SvrIpcWithServiceWatchOverride = {
        param(
            [Parameter(Mandatory = $true)][string]$Method,
            [string]$PayloadJson,
            [Parameter(Mandatory = $true)][int]$WallClockTimeoutMs,
            [Parameter(Mandatory = $true)][int]$ServicePidBaseline,
            [int]$ServicePidPollMs = 1000,
            [string]$HelpersRoot = $PSScriptRoot
        )

        if ($Method -ne 'RunDiagnostic') {
            throw "Unexpected watch method in lifecycle mock: $Method"
        }

        $name = 'mock-diagnostic'
        if (-not [string]::IsNullOrWhiteSpace($PayloadJson)) {
            $doc = $PayloadJson | ConvertFrom-Json
            if ($doc.name) { $name = [string]$doc.name }
        }

        $outcome = 'Pass'
        if ($script:VpnLifecycleMockOutcomeByDiagnostic.ContainsKey($name)) {
            $outcome = [string]$script:VpnLifecycleMockOutcomeByDiagnostic[$name]
        }

        $message = switch ($name) {
            'vpn-resource-health' { 'healthy=true summary=Healthy' }
            'vpn-connection-routing-smoke' { 'PER-PROCESS ISOLATION: PASS. mock routing smoke' }
            'vpn-lifecycle-reconnect-stress' {
                if ($outcome -eq 'Pass') {
                    "phase=connect-2 vpnRoutingReady=true`nphase=connect-2-routing-smoke pass`nphase=disconnect-2 cleanup=pass"
                }
                else {
                    "phase=connect-2 vpnRoutingReady=true`nphase=connect-2-routing-smoke fail`nrouting smoke failed"
                }
            }
            'vpn-lifecycle-cleanup-check' { 'vpnRoutingReady=false cleanup detailed pass' }
            default { ("mock pass for " + $name) }
        }

        $payload = New-SvrMockDiagnosticResultPayload -Outcome $outcome -Message $message
        return New-SvrSparseIpcEnvelope -Ok $true -PayloadJson $payload
    }
}

function Clear-VpnLifecycleSequenceMock {
    Clear-SvrRuntimeDiagnosticMockOverrides
    $script:SvrServiceProcessIdOverride = $null
    $script:VpnLifecycleMockOutcomeByDiagnostic = @{}
}

function Invoke-VpnLifecycleRunnerCore {
    . (Join-Path $scriptsRoot "_vpn-lifecycle-runner-core.ps1")
    return Get-SvrRunnerExitCode
}

# Static: reconnect stress must run routing smoke before final disconnect
$lifecycleCs = Join-Path (Get-SvrRepoRoot) "src\SelectiveVpnRouter.Service\DriverAndIsolationTests.Lifecycle.cs"
$lifecycleText = Get-Content -LiteralPath $lifecycleCs -Raw
Assert-True ($lifecycleText -match 'phase=connect-2-routing-smoke pass') 'Lifecycle.cs includes post-reconnect routing smoke pass marker'

$expectedOrder = Get-VpnLifecycleAcceptanceDiagnosticNames
Assert-Equal 5 $expectedOrder.Count 'acceptance diagnostic count'
Assert-Equal 'vpn-connection-routing-smoke' $expectedOrder[1] 'initial smoke before stress'
Assert-Equal 'vpn-lifecycle-cleanup-check' $expectedOrder[3] 'cleanup after stress'

# Happy path full sequence
Register-VpnLifecycleSequenceMock
try {
    $null = Initialize-VpnLifecycleMockHost
    $exit = Invoke-VpnLifecycleRunnerCore
    Assert-Equal 0 $exit 'happy path exit'
}
finally {
    Clear-VpnLifecycleSequenceMock
}

# Negative: reconnect ready but embedded routing smoke fails => overall FAIL
Register-VpnLifecycleSequenceMock -OutcomeByDiagnostic @{ 'vpn-lifecycle-reconnect-stress' = 'Fail' }
try {
    $null = Initialize-VpnLifecycleMockHost
    $exit = Invoke-VpnLifecycleRunnerCore
    Assert-Equal 3 $exit 'routing smoke fail exit'
}
finally {
    Clear-VpnLifecycleSequenceMock
}

# Warn on standalone smoke is not acceptable
Register-VpnLifecycleSequenceMock -OutcomeByDiagnostic @{ 'vpn-connection-routing-smoke' = 'Warn' }
try {
    $null = Initialize-VpnLifecycleMockHost
    $exit = Invoke-VpnLifecycleRunnerCore
    Assert-Equal 3 $exit 'warn smoke exit'
}
finally {
    Clear-VpnLifecycleSequenceMock
}

Write-Host 'PASS test-vpn-lifecycle-sequence-mock'
exit 0
