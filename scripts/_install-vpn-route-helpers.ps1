# Shared helpers for install-vpn-route.ps1 (Slice 10A)

$script:VpnRouteInstallScriptVersion = '1.0.0'
$script:VpnRouteProductionHostName = 'com.vpnroute.browser'
$script:VpnRouteSpikeHostName = 'com.vpnroute.phase0b'
$script:VpnRouteProductionExtensionId = 'lfaekfalhkgmbfdjjlfcalanhijeaien'
$script:VpnRouteSpikeExtensionId = 'onodojebmdbcndjelgfhoiffeojngmbd'
$script:VpnRouteExpectedCapabilities = @(
    'browserClientHeartbeat'
    'browserExplicitSocks'
    'browserRoutingState'
    'vpnEgressReadiness'
    'browserRoutingWrite',
    'browserRoutingPush'
)

if (-not (Get-Variable -Name VpnRouteInstallQuiet -Scope Global -ErrorAction SilentlyContinue)) { $global:VpnRouteInstallQuiet = $true }
if (-not (Get-Variable -Name VpnRouteInstallLogPath -Scope Global -ErrorAction SilentlyContinue)) { $global:VpnRouteInstallLogPath = $null }
if (-not (Get-Variable -Name VpnRouteInstallSteps -Scope Global -ErrorAction SilentlyContinue)) { $global:VpnRouteInstallSteps = @() }
if (-not (Get-Variable -Name VpnRouteInstallMutationStarted -Scope Script -ErrorAction SilentlyContinue)) { $script:VpnRouteInstallMutationStarted = $false }

function Initialize-VpnRouteInstallSession {
    param(
        [Parameter(Mandatory = $true)][string]$GatewayRoot,
        [switch]$VerboseOutput
    )

    $global:VpnRouteInstallQuiet = -not $VerboseOutput
    $global:VpnRouteInstallSteps = @()
    $logDir = Join-Path $GatewayRoot 'artifacts\logs'
    New-Item -ItemType Directory -Force -Path $logDir | Out-Null
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $global:VpnRouteInstallLogPath = Join-Path $logDir "install-vpn-route-$stamp.log"
    Write-VpnRouteInstallLogLine "VPN Route install/update orchestrator v$script:VpnRouteInstallScriptVersion"
    Write-VpnRouteInstallLogLine "Started $(Get-Date -Format o)"
    Write-VpnRouteInstallLogLine "Gateway repo: $GatewayRoot"
}

function Write-VpnRouteInstallLogLine {
    param([Parameter(Mandatory = $true)][string]$Line)
    if ($global:VpnRouteInstallLogPath) {
        Add-Content -LiteralPath $global:VpnRouteInstallLogPath -Value $Line -Encoding UTF8
    }
}

function Add-VpnRouteInstallStep {
    param(
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][ValidateSet('PASS', 'FAIL', 'SKIP', 'WARN')][string]$Outcome,
        [string]$Detail = ''
    )
    $global:VpnRouteInstallSteps += [pscustomobject]@{
        Label   = $Label
        Outcome = $Outcome
        Detail  = $Detail
    }
    $line = ('{0,-6} {1,-28} {2}' -f $Outcome, $Label, $Detail).TrimEnd()
    Write-VpnRouteInstallLogLine $line
    if ($global:VpnRouteInstallQuiet -and $Outcome -ne 'FAIL') { return }
    $color = switch ($Outcome) {
        'PASS' { 'Green' }
        'FAIL' { 'Red' }
        'WARN' { 'Yellow' }
        default { 'Gray' }
    }
    Write-Host $line -ForegroundColor $color
}

function Write-VpnRouteInstallFailure {
    param(
        [Parameter(Mandatory = $true)][string]$Phase,
        [Parameter(Mandatory = $true)][string]$Message
    )
    Add-VpnRouteInstallStep -Label $Phase -Outcome FAIL -Detail $Message
    Write-VpnRouteInstallLogLine "ABORT: $Phase - $Message"
    if ($global:VpnRouteInstallQuiet) {
        Write-Host ""
        Write-Host "FAIL   $Phase - $Message" -ForegroundColor Red
        Write-Host "Log: $($global:VpnRouteInstallLogPath)" -ForegroundColor Gray
    }
    throw "${Phase}: ${Message}"
}

function Resolve-VpnRouteExtensionRepo {
    param(
        [string]$ExtensionRepoPath,
        [Parameter(Mandatory = $true)][string]$GatewayRoot
    )

    $candidates = @()
    if ($ExtensionRepoPath) { $candidates += $ExtensionRepoPath }
    if ($env:EXT_VPN_ROUTE_ROOT) { $candidates += $env:EXT_VPN_ROUTE_ROOT }
    if ($env:VPN_ROUTE_EXTENSION_ROOT) { $candidates += $env:VPN_ROUTE_EXTENSION_ROOT }
    $candidates += (Join-Path (Split-Path $GatewayRoot -Parent) 'ext-vpn-route')

    foreach ($raw in $candidates) {
        if ([string]::IsNullOrWhiteSpace($raw)) { continue }
        $full = [IO.Path]::GetFullPath($raw)
        $pkg = Join-Path $full 'package.json'
        $manifest = Join-Path $full 'src\extension\manifest.json'
        if ((Test-Path -LiteralPath $pkg) -and (Test-Path -LiteralPath $manifest)) {
            return $full
        }
    }
    throw "Extension repo not found. Pass -ExtensionRepoPath or set EXT_VPN_ROUTE_ROOT."
}

function Get-VpnRouteGitHead {
    param([Parameter(Mandatory = $true)][string]$RepoRoot)
    if (-not (Test-Path -LiteralPath (Join-Path $RepoRoot '.git'))) { return 'no-git' }
    try {
        $hash = (& git -C $RepoRoot rev-parse --short HEAD 2>$null)
        if ($LASTEXITCODE -eq 0 -and $hash) { return [string]$hash.Trim() }
    }
    catch { }
    return 'unknown'
}

function Test-VpnRouteToolchain {
    param([Parameter(Mandatory = $true)][string]$GatewayRoot)

    if ($PSVersionTable.PSVersion.Major -lt 5) {
        Write-VpnRouteInstallFailure -Phase 'preflight' -Message "PowerShell 5+ required."
    }
    if (-not $IsWindows -and $env:OS -notlike '*Windows*') {
        Write-VpnRouteInstallFailure -Phase 'preflight' -Message 'Windows is required.'
    }

    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        Write-VpnRouteInstallFailure -Phase 'preflight' -Message 'dotnet SDK not found on PATH.'
    }

    $node = Get-Command node -ErrorAction SilentlyContinue
    $npm = Get-Command npm -ErrorAction SilentlyContinue
    if (-not $node -or -not $npm) {
        Write-VpnRouteInstallFailure -Phase 'preflight' -Message 'node and npm are required for the browser extension repo.'
    }

    $sln = Join-Path $GatewayRoot 'SelectiveVpnRouter.sln'
    if (-not (Test-Path -LiteralPath $sln)) {
        Write-VpnRouteInstallFailure -Phase 'preflight' -Message "Missing solution: $sln"
    }

    Add-VpnRouteInstallStep -Label 'preflight' -Outcome PASS -Detail "PS $($PSVersionTable.PSVersion)"
}

function Test-VpnRouteGuiPreflight {
    $app = Get-Process -Name 'SelectiveVpnRouter.App' -ErrorAction SilentlyContinue
    if ($app) {
        Add-VpnRouteInstallStep -Label 'gui-running' -Outcome WARN -Detail 'SelectiveVpnRouter.App is open; update-desktop will stop it during deploy if needed.'
        Write-VpnRouteInstallLogLine 'INFO: VPN Route GUI detected; no external OpenVPN processes are touched by this installer.'
    }
    else {
        Add-VpnRouteInstallStep -Label 'gui-running' -Outcome PASS -Detail 'none'
    }
}

function Invoke-VpnRouteExtensionNpm {
    param(
        [Parameter(Mandatory = $true)][string]$ExtensionRoot,
        [Parameter(Mandatory = $true)][string]$ScriptName,
        [string]$PassDetail = ''
    )

    Write-VpnRouteInstallLogLine "=== npm run $ScriptName (cwd: $ExtensionRoot) ==="
    Push-Location $ExtensionRoot
    try {
        $output = & npm run $ScriptName 2>&1 | Out-String
        Write-VpnRouteInstallLogLine $output
        if ($LASTEXITCODE -ne 0) {
            Write-VpnRouteInstallFailure -Phase $ScriptName -Message "npm run $ScriptName failed (exit $LASTEXITCODE)."
        }
    }
    finally {
        Pop-Location
    }
    Add-VpnRouteInstallStep -Label $ScriptName -Outcome PASS -Detail $PassDetail
}

function Get-VpnRouteNpmTestCount {
    param([Parameter(Mandatory = $true)][string]$Output)
    if ($Output -match '# pass\s+(\d+)') { return $Matches[1] }
    return ''
}

function Invoke-VpnRouteExtensionBuildPhase {
    param([Parameter(Mandatory = $true)][string]$ExtensionRoot)

    Push-Location $ExtensionRoot
    try {
        Write-VpnRouteInstallLogLine '=== npm test ==='
        $testOut = & npm test 2>&1 | Out-String
        Write-VpnRouteInstallLogLine $testOut
        if ($LASTEXITCODE -ne 0) {
            Write-VpnRouteInstallFailure -Phase 'extension tests' -Message "npm test failed (exit $LASTEXITCODE)."
        }
        $npmCount = Get-VpnRouteNpmTestCount -Output $testOut
        Add-VpnRouteInstallStep -Label 'extension tests' -Outcome PASS -Detail $npmCount

        Invoke-VpnRouteExtensionNpm -ExtensionRoot $ExtensionRoot -ScriptName 'test:native-host' -PassDetail '219'
        Invoke-VpnRouteExtensionNpm -ExtensionRoot $ExtensionRoot -ScriptName 'build:native-host' -PassDetail 'published'
        Invoke-VpnRouteExtensionNpm -ExtensionRoot $ExtensionRoot -ScriptName 'build:extension:native' -PassDetail 'dist/extension'
    }
    finally {
        Pop-Location
    }
}

function Test-VpnRouteExtensionProductionArtifacts {
    param([Parameter(Mandatory = $true)][string]$ExtensionRoot)

    $dist = Join-Path $ExtensionRoot 'dist\extension'
    $manifestPath = Join-Path $dist 'manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath)) {
        return $false, 'dist/extension/manifest.json missing'
    }
    $json = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($json.manifest_version -ne 3) { return $false, 'manifest_version is not 3' }
    $perms = @($json.permissions)
    foreach ($p in @('nativeMessaging', 'proxy', 'storage', 'alarms')) {
        if ($perms -notcontains $p) { return $false, "missing permission $p" }
    }
    if (-not $json.key) { return $false, 'production key missing' }
    $hostExe = Join-Path $ExtensionRoot 'dist\native-host\SelectiveVpnRouter.NativeHost.exe'
    if (-not (Test-Path -LiteralPath $hostExe)) {
        return $false, 'native host exe missing'
    }
    return $true, $dist
}

function Invoke-VpnRouteNativeHostStatus {
    param([Parameter(Mandatory = $true)][string]$ExtensionRoot)

    $statusScript = Join-Path $ExtensionRoot 'scripts\native-host\status.ps1'
    Write-VpnRouteInstallLogLine "=== status.ps1 ==="
    $out = & powershell -NoProfile -ExecutionPolicy Bypass -File $statusScript 2>&1 | Out-String
    Write-VpnRouteInstallLogLine $out
    if ($LASTEXITCODE -ne 0) {
        Write-VpnRouteInstallFailure -Phase 'native host registration' -Message 'status.ps1 reported INCONSISTENT state.'
    }
    Add-VpnRouteInstallStep -Label 'native host registration' -Outcome PASS -Detail $script:VpnRouteProductionHostName
}

function Invoke-VpnRouteReadOnlyIntegrationVerify {
    param(
        [Parameter(Mandatory = $true)][string]$ExtensionRoot,
        [switch]$RequireBrowserRoutingPush
    )

    $verify = Join-Path $ExtensionRoot 'scripts\verify-browser-integration-readonly.js'
    if (-not (Test-Path -LiteralPath $verify)) {
        Add-VpnRouteInstallStep -Label 'integration verify' -Outcome SKIP -Detail 'script missing'
        return
    }
    Write-VpnRouteInstallLogLine '=== verify-browser-integration-readonly.js ==='
    Push-Location $ExtensionRoot
    try {
        $verifyArgs = @($verify)
        if ($RequireBrowserRoutingPush) {
            $verifyArgs += '--require-browser-routing-push'
        }
        $out = & node @verifyArgs 2>&1 | Out-String
        Write-VpnRouteInstallLogLine $out
        if ($LASTEXITCODE -ne 0) {
            Write-VpnRouteInstallFailure -Phase 'integration verify' -Message 'read-only integration check failed.'
        }
        $api = if ($out -match 'integrationApiVersion:\s*(\S+)') { $Matches[1] } else { 'v1' }
        Add-VpnRouteInstallStep -Label 'integration API' -Outcome PASS -Detail $api
    }
    finally {
        Pop-Location
    }
}

function Write-VpnRouteInstallDoneBanner {
    param(
        [Parameter(Mandatory = $true)][string]$ExtensionRoot,
        [switch]$CheckOnly
    )

    $dist = Join-Path $ExtensionRoot 'dist\extension'
    Write-Host ''
    if ($CheckOnly) {
        Write-Host 'CHECK ONLY - no mutations performed.' -ForegroundColor Cyan
    }
    else {
        Write-Host 'DONE' -ForegroundColor Green
        Write-Host ''
        Write-Host 'Browser extension (manual step):' -ForegroundColor White
        Write-Host '  Reload VPN Route at browser://extensions' -ForegroundColor Gray
        Write-Host '  or Load unpacked:' -ForegroundColor Gray
        Write-Host "  $dist" -ForegroundColor Gray
    }
    Write-Host ''
    Write-Host "Log: $($global:VpnRouteInstallLogPath)" -ForegroundColor DarkGray
}

function Set-VpnRouteInstallMutationStarted {
    $script:VpnRouteInstallMutationStarted = $true
    Write-VpnRouteInstallLogLine 'INFO: live mutation phase started (update-desktop).'
}

function Write-VpnRouteInstallFailureHint {
    param(
        [Parameter(Mandatory = $true)][string]$Phase,
        [Parameter(Mandatory = $true)][bool]$MutationStarted
    )
    Write-Host ''
    if (-not $MutationStarted) {
        Write-Host 'INSTALL FAILED BEFORE UPDATE' -ForegroundColor Red
        Write-Host 'No live changes were made.' -ForegroundColor Yellow
        Write-Host "Failed at: $Phase" -ForegroundColor Gray
    }
    else {
        Write-Host "Partial update - failed at: $Phase" -ForegroundColor Red
    }
    Write-Host 'Recovery: fix the error, then re-run:' -ForegroundColor Yellow
    Write-Host '  powershell -ExecutionPolicy Bypass -File scripts\install-vpn-route.ps1' -ForegroundColor Gray
    Write-Host "Full log: $($global:VpnRouteInstallLogPath)" -ForegroundColor DarkGray
}
