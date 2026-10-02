$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$scriptsRoot = $PSScriptRoot
. (Join-Path $scriptsRoot "_common.ps1")
. (Join-Path $scriptsRoot "_update-helpers.ps1")
. (Join-Path $scriptsRoot "_runtime-diagnostic-report-state.ps1")
. (Join-Path $scriptsRoot "_runtime-diagnostic-report.ps1")
. (Join-Path $scriptsRoot "_runtime-diagnostic-mock-helpers.ps1")
. (Join-Path $scriptsRoot "_multi-app-routing-acceptance.ps1")

function Assert-True { param([bool]$C, [string]$M) if (-not $C) { throw $M } }
function Assert-Equal { param($E, $A, [string]$M) if ($E -ne $A) { throw "$M expected=$E actual=$A" } }

function Initialize-MultiAppRoutingMockHost {
    $repoRoot = Get-SvrRepoRoot
    $reportPath = Join-Path $env:TEMP ("svr-multi-app-seq-" + [Guid]::NewGuid().ToString("N") + ".txt")
    Initialize-SvrRuntimeDiagnosticReportState -StartedAt (Get-Date) -RepoRoot $repoRoot -ReportPath $reportPath
    Set-Variable -Scope Script -Name IpcReadinessSeconds -Value 5
    Set-Variable -Scope Script -Name WaitConnectedSeconds -Value 1
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

function Register-MultiAppRoutingSequenceMock {
    param([string]$Message, [string]$Outcome = "Pass")

    $script:MultiAppRoutingMockMessage = $Message
    $script:MultiAppRoutingMockOutcome = $Outcome
    $script:SvrServiceProcessIdOverride = { return 5151 }

    $script:SvrIpcInvokeOverride = {
        param([Parameter(Mandatory = $true)][string]$Method, [string]$PayloadJson, [int]$TimeoutMs = 15000, [int]$WallClockTimeoutMs = 0)
        if ($Method -ne "GetStatus") { throw "Unexpected Invoke-SvrIpc method: $Method" }
        return New-SvrSparseIpcEnvelope -Ok $true -PayloadJson (New-SvrMockGetStatusPayload -VpnRoutingReady $true -DriverLoaded $true)
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
        if ($Method -ne "RunDiagnostic") { throw "Unexpected watch method: $Method" }
        $payload = New-SvrMockDiagnosticResultPayload -Outcome $script:MultiAppRoutingMockOutcome -Message $script:MultiAppRoutingMockMessage
        return New-SvrSparseIpcEnvelope -Ok $true -PayloadJson $payload
    }
}

function Clear-MultiAppRoutingSequenceMock {
    Clear-SvrRuntimeDiagnosticMockOverrides
    $script:SvrServiceProcessIdOverride = $null
    $script:MultiAppRoutingMockMessage = $null
    $script:MultiAppRoutingMockOutcome = $null
}

function Invoke-MultiAppRoutingRunnerCore {
    . (Join-Path $scriptsRoot "_multi-app-routing-runner-core.ps1")
    return Get-SvrRunnerExitCode
}

$diagName = Get-MultiAppRoutingDiagnosticName
Assert-Equal "multi-app-routing-isolation" $diagName "diagnostic name"

Register-MultiAppRoutingSequenceMock -Message (New-SvrMockMultiAppRoutingPassMessage)
try {
    $null = Initialize-MultiAppRoutingMockHost
    $exit = Invoke-MultiAppRoutingRunnerCore
    Assert-Equal 0 $exit "happy path exit"
}
finally {
    Clear-MultiAppRoutingSequenceMock
}

Register-MultiAppRoutingSequenceMock -Message (New-SvrMockMultiAppRoutingFailMessage -Scenario "B-Direct") -Outcome "Pass"
try {
    $null = Initialize-MultiAppRoutingMockHost
    $exit = Invoke-MultiAppRoutingRunnerCore
    Assert-Equal 3 $exit "B direct negative exit"
}
finally {
    Clear-MultiAppRoutingSequenceMock
}

Register-MultiAppRoutingSequenceMock -Message (New-SvrMockMultiAppRoutingFailMessage -Scenario "C-Proxy") -Outcome "Pass"
try {
    $null = Initialize-MultiAppRoutingMockHost
    $exit = Invoke-MultiAppRoutingRunnerCore
    Assert-Equal 3 $exit "C proxy negative exit"
}
finally {
    Clear-MultiAppRoutingSequenceMock
}

Register-MultiAppRoutingSequenceMock -Message (New-SvrMockMultiAppRoutingPassMessage) -Outcome "Fail"
try {
    $null = Initialize-MultiAppRoutingMockHost
    $exit = Invoke-MultiAppRoutingRunnerCore
    Assert-Equal 3 $exit "Fail outcome exit"
}
finally {
    Clear-MultiAppRoutingSequenceMock
}

Write-Host "PASS test-multi-app-routing-sequence-mock"
exit 0
