#Requires -Version 5.1
param(
    [int]$IpcReadinessSeconds = 30,
    [int]$WaitConnectedSeconds = 300,
    [int]$PollIntervalMs = 2000,
    [int]$StatusTimeoutMs = 15000,
    [int]$IpcAttemptTimeoutMs = 5000,
    [int]$DiagnosticTimeoutMs = 900000,
    [int]$DiagnosticServicePollMs = 1000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "_common.ps1")
. (Join-Path $PSScriptRoot "_update-helpers.ps1")
. (Join-Path $PSScriptRoot "_runtime-diagnostic-report-state.ps1")
. (Join-Path $PSScriptRoot "_runtime-diagnostic-report.ps1")
. (Join-Path $PSScriptRoot "_multi-app-routing-acceptance.ps1")

Assert-SvrRunnerIpcDependencies

$repoRoot = Get-SvrRepoRoot
$logDir = Join-Path $repoRoot "artifacts\logs"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$reportPath = Join-Path $logDir "multi-app-routing-diagnostic-latest.txt"

$startedAt = Get-Date
Initialize-SvrRuntimeDiagnosticReportState -StartedAt $startedAt -RepoRoot $repoRoot -ReportPath $reportPath
Set-Variable -Scope Script -Name IpcReadinessSeconds -Value $IpcReadinessSeconds
Set-Variable -Scope Script -Name WaitConnectedSeconds -Value $WaitConnectedSeconds
Set-Variable -Scope Script -Name PollIntervalMs -Value $PollIntervalMs
Set-Variable -Scope Script -Name StatusTimeoutMs -Value $StatusTimeoutMs
Set-Variable -Scope Script -Name IpcAttemptTimeoutMs -Value $IpcAttemptTimeoutMs
Set-Variable -Scope Script -Name DiagnosticTimeoutMs -Value $DiagnosticTimeoutMs
Set-Variable -Scope Script -Name DiagnosticServicePollMs -Value $DiagnosticServicePollMs

function Add-Line {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Line)
    Add-SvrRuntimeDiagnosticLine -Line $Line
}
function Flush-Report { param([int]$FinalExitCode = 3) Flush-SvrRuntimeDiagnosticReport -FinalExitCode $FinalExitCode }
function Save-Report { param([Parameter(Mandatory = $true)][int]$FinalExitCode) Save-SvrRuntimeDiagnosticReport -FinalExitCode $FinalExitCode }

. (Join-Path $PSScriptRoot "_multi-app-routing-runner-core.ps1")

exit (Get-SvrRunnerExitCode)
