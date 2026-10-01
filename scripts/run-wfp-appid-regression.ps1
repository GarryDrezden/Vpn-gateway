#Requires -Version 5.1
<#
.SYNOPSIS
  Offline WFP APP_ID regression runner (normalization matrix, blob/case, unicode probe, Telegram acceptance).

.DESCRIPTION
  Does not toggle VPN. Requires VPN Route Connected, callout driver loaded, elevated service.
  Delegates to run-runtime-diagnostic.ps1 with the APP_ID regression diagnostic set.
#>

param(
    [string]$TelegramExePath = "",
    [int]$IpcReadinessSeconds = 30,
    [int]$WaitConnectedSeconds = 300,
    [int]$DiagnosticTimeoutMs = 300000
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$runner = Join-Path $PSScriptRoot "run-runtime-diagnostic.ps1"
$telegram = $TelegramExePath
if ([string]::IsNullOrWhiteSpace($telegram)) {
    $telegram = Join-Path $env:APPDATA "Telegram Desktop\Telegram.exe"
}

$names = @(
    "wfp-runtime-appid-normalization-matrix",
    "wfp-runtime-appid-blob",
    "wfp-runtime-appid-case",
    "wfp-probe-unicode-redirect",
    "wfp-telegram-appid-acceptance"
)

& $runner `
    -DiagnosticNames $names `
    -TelegramExePath $telegram `
    -IpcReadinessSeconds $IpcReadinessSeconds `
    -WaitConnectedSeconds $WaitConnectedSeconds `
    -DiagnosticTimeoutMs $DiagnosticTimeoutMs

exit $LASTEXITCODE