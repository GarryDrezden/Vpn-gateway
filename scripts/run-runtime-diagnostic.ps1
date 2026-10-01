#Requires -Version 5.1
<#
.SYNOPSIS
  Offline runtime WFP/APP_ID diagnostics via VPN Route IPC (no Connect/Disconnect/VPN toggles).

.DESCRIPTION
  User workflow:
    1. Disable external VPN (if needed for VPN Route Connect).
    2. Connect VPN Route manually in the app.
    3. Run: .\scripts\run-runtime-diagnostic.ps1
    4. Re-enable external VPN; share artifacts\logs\runtime-diagnostic-latest.txt with Cursor.

  Exit codes:
    0 = all diagnostics executed (IPC + RunDiagnostic completed)
    1 = VPN Route did not reach Connected within timeout
    2 = callout driver not loaded
    3 = infrastructure failure (IPC, exceptions, RunDiagnostic transport errors)
#>

param(
    [string[]]$DiagnosticNames = @(
        "wfp-runtime-appid-blob",
        "wfp-runtime-appid-case"
    ),
    [int]$IpcReadinessSeconds = 30,
    [int]$WaitConnectedSeconds = 300,
    [int]$PollIntervalMs = 2000,
    [int]$StatusTimeoutMs = 15000,
    [int]$IpcAttemptTimeoutMs = 5000,
    [int]$DiagnosticTimeoutMs = 180000,
    [string]$TelegramExePath = ""
    ,[int]$DiagnosticServicePollMs = 1000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "_common.ps1")
. (Join-Path $PSScriptRoot "_update-helpers.ps1")
. (Join-Path $PSScriptRoot "_runtime-diagnostic-report-state.ps1")
. (Join-Path $PSScriptRoot "_runtime-diagnostic-report.ps1")

Assert-SvrRunnerIpcDependencies

$repoRoot = Get-SvrRepoRoot
$logDir = Join-Path $repoRoot "artifacts\logs"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$reportPath = Join-Path $logDir "runtime-diagnostic-latest.txt"

$startedAt = Get-Date
Initialize-SvrRuntimeDiagnosticReportState -StartedAt $startedAt -RepoRoot $repoRoot -ReportPath $reportPath
Set-Variable -Scope Script -Name IpcReadinessSeconds -Value $IpcReadinessSeconds
Set-Variable -Scope Script -Name WaitConnectedSeconds -Value $WaitConnectedSeconds
Set-Variable -Scope Script -Name DiagnosticNames -Value $DiagnosticNames
if (-not [string]::IsNullOrWhiteSpace($TelegramExePath)) {
    Set-Variable -Scope Script -Name SvrRegressionTelegramExePath -Value $TelegramExePath
}
elseif (Test-Path -LiteralPath (Join-Path $env:APPDATA "Telegram Desktop\Telegram.exe")) {
    Set-Variable -Scope Script -Name SvrRegressionTelegramExePath -Value (Join-Path $env:APPDATA "Telegram Desktop\Telegram.exe")
}

function Add-Line {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Line
    )
    Add-SvrRuntimeDiagnosticLine -Line $Line
}

function Flush-Report {
    param([int]$FinalExitCode = 3)
    Flush-SvrRuntimeDiagnosticReport -FinalExitCode $FinalExitCode
}

function Save-Report {
    param([Parameter(Mandatory = $true)][int]$FinalExitCode)
    Save-SvrRuntimeDiagnosticReport -FinalExitCode $FinalExitCode
}

. (Join-Path $PSScriptRoot "_runtime-diagnostic-runner-core.ps1")

exit (Get-SvrRunnerExitCode)
