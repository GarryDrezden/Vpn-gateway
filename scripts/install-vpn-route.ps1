#Requires -Version 5.1
<#
.SYNOPSIS
  One supported developer install/update workflow for VPN Route (Slice 10A).

.DESCRIPTION
  Build-first orchestration:
    extension repo tests + native host + production extension bundle
    vpn-gateway update-desktop (Service/App publish)
    native host registration (HKCU)
    read-only integration verification

  Does NOT load/reload the Chromium extension automatically.
  Does NOT manipulate external OpenVPN processes.

.PARAMETER ExtensionRepoPath
  Path to ext-vpn-route. Default: sibling of vpn-gateway or EXT_VPN_ROUTE_ROOT.

.PARAMETER CheckOnly
  Report status only; no builds, no Service deploy, no registry writes.

.PARAMETER VerboseOutput
  Show PASS lines on console (full detail always in artifacts/logs).
#>
[CmdletBinding()]
param(
    [string]$ExtensionRepoPath,
    [switch]$CheckOnly,
    [switch]$VerboseOutput
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_common.ps1')
. (Join-Path $PSScriptRoot '_install-vpn-route-helpers.ps1')

$gatewayRoot = Get-SvrRepoRoot
$extensionRoot = Resolve-VpnRouteExtensionRepo -ExtensionRepoPath $ExtensionRepoPath -GatewayRoot $gatewayRoot
Initialize-VpnRouteInstallSession -GatewayRoot $gatewayRoot -VerboseOutput:$VerboseOutput

Write-VpnRouteInstallLogLine "Extension repo: $extensionRoot"
Write-VpnRouteInstallLogLine "Gateway commit: $(Get-VpnRouteGitHead -RepoRoot $gatewayRoot)"
Write-VpnRouteInstallLogLine "Extension commit: $(Get-VpnRouteGitHead -RepoRoot $extensionRoot)"
Write-VpnRouteInstallLogLine "Mode: $(if ($CheckOnly) { 'CheckOnly' } else { 'Install/Update' })"

$failedPhase = $null
try {
    Test-VpnRouteToolchain -GatewayRoot $gatewayRoot
    Test-VpnRouteGuiPreflight

    if ($CheckOnly) {
        $svcName = 'SelectiveVpnRouter'
        $svc = Get-Service -Name $svcName -ErrorAction SilentlyContinue
        if ($svc) {
            Add-VpnRouteInstallStep -Label 'service' -Outcome PASS -Detail $svc.Status.ToString()
        }
        else {
            Add-VpnRouteInstallStep -Label 'service' -Outcome WARN -Detail 'not installed'
        }

        $okArt, $artDetail = Test-VpnRouteExtensionProductionArtifacts -ExtensionRoot $extensionRoot
        if ($okArt) {
            Add-VpnRouteInstallStep -Label 'extension build' -Outcome PASS -Detail 'present'
        }
        else {
            Add-VpnRouteInstallStep -Label 'extension build' -Outcome WARN -Detail $artDetail
        }

        $statusScript = Join-Path $extensionRoot 'scripts\native-host\status.ps1'
        if (Test-Path -LiteralPath $statusScript) {
            $out = & powershell -NoProfile -ExecutionPolicy Bypass -File $statusScript 2>&1 | Out-String
            Write-VpnRouteInstallLogLine $out
            $regOutcome = if ($LASTEXITCODE -eq 0) { 'PASS' } else { 'WARN' }
            Add-VpnRouteInstallStep -Label 'native host status' -Outcome $regOutcome -Detail $(if ($LASTEXITCODE -eq 0) { 'consistent' } else { 'see log' })
        }

        if ($svc -and $svc.Status -eq 'Running') {
            Invoke-VpnRouteReadOnlyIntegrationVerify -ExtensionRoot $extensionRoot
        }
        else {
            Add-VpnRouteInstallStep -Label 'integration verify' -Outcome SKIP -Detail 'service not running'
        }

        Write-VpnRouteInstallLogLine 'Yandex note: production registration uses HKCU\Software\Google\Chrome\NativeMessagingHosts\com.vpnroute.browser (documented Phase 0B/5 acceptance for current Yandex).'
        Write-VpnRouteInstallDoneBanner -ExtensionRoot $extensionRoot -CheckOnly
        exit 0
    }

    if (-not (Test-SvrElevated)) {
        Write-VpnRouteInstallFailure -Phase 'preflight' -Message 'Install/Update requires an elevated PowerShell (Service publish uses update-desktop.ps1). Use -CheckOnly for read-only audit without admin.'
    }

    # Build extension stack before mutating installed Service/registration.
    Invoke-VpnRouteExtensionBuildPhase -ExtensionRoot $extensionRoot

    Set-VpnRouteInstallMutationStarted

    $updateScript = Join-Path $PSScriptRoot 'update-desktop.ps1'
    Write-VpnRouteInstallLogLine '=== update-desktop.ps1 ==='
    & $updateScript -VerboseOutput:$VerboseOutput
    if ($LASTEXITCODE -ne 0) {
        Write-VpnRouteInstallFailure -Phase 'desktop update' -Message "update-desktop.ps1 exit $LASTEXITCODE"
    }
    Add-VpnRouteInstallStep -Label 'desktop publish' -Outcome PASS -Detail 'see update-desktop log'

    $svc = Get-Service -Name 'SelectiveVpnRouter' -ErrorAction SilentlyContinue
    if ($svc) {
        Add-VpnRouteInstallStep -Label 'service' -Outcome PASS -Detail $svc.Status.ToString()
    }

    $registerScript = Join-Path $extensionRoot 'scripts\native-host\register.ps1'
    Write-VpnRouteInstallLogLine '=== register.ps1 ==='
    & powershell -NoProfile -ExecutionPolicy Bypass -File $registerScript -Target Chrome
    if ($LASTEXITCODE -ne 0) {
        Write-VpnRouteInstallFailure -Phase 'native host register' -Message 'register.ps1 failed after Service update succeeded.'
    }
    Invoke-VpnRouteNativeHostStatus -ExtensionRoot $extensionRoot

    $okArt, $dist = Test-VpnRouteExtensionProductionArtifacts -ExtensionRoot $extensionRoot
    if (-not $okArt) {
        Write-VpnRouteInstallFailure -Phase 'extension build' -Message $dist
    }
    Add-VpnRouteInstallStep -Label 'extension build' -Outcome PASS -Detail $script:VpnRouteProductionExtensionId

    Invoke-VpnRouteReadOnlyIntegrationVerify -ExtensionRoot $extensionRoot

    Write-VpnRouteInstallLogLine "Browser registration: HKCU\Software\Google\Chrome\NativeMessagingHosts\$($script:VpnRouteProductionHostName) (Yandex reads Chrome key per repo acceptance docs)."
    Write-VpnRouteInstallDoneBanner -ExtensionRoot $extensionRoot
    exit 0
}
catch {
    $failedPhase = if ($_.Exception.Message -match '^([^:]+):') { $Matches[1] } else { 'install' }
    Write-VpnRouteInstallLogLine $_.Exception.Message
    Write-VpnRouteInstallLogLine $_.ScriptStackTrace
    Write-VpnRouteInstallFailureHint -Phase $failedPhase -MutationStarted $script:VpnRouteInstallMutationStarted
    exit 1
}
