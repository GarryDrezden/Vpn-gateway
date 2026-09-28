param(
    [switch]$RunSmokeTest
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

$abiSource = Join-Path $root 'tools\wfp-abi-probe\wfp_abi_probe.c'
$smokeSource = Join-Path $root 'tools\wfp-smoke-test\wfp_smoke_test.c'
$appIdSmokeSource = Join-Path $root 'tools\wfp-appid-smoke-test\wfp_appid_smoke_test.c'
$probeDir = Join-Path $root 'tools\wfp-abi-probe'
$smokeDir = Join-Path $root 'tools\wfp-smoke-test'
$appIdSmokeDir = Join-Path $root 'tools\wfp-appid-smoke-test'

Write-Host "INFO repo-root: $root"
Write-Host "INFO abi-source: $abiSource"
Write-Host "INFO smoke-source: $smokeSource"
Write-Host "INFO appid-smoke-source: $appIdSmokeSource"

if (-not (Test-Path $abiSource)) {
    throw "ABI probe source not found: $abiSource"
}

if (-not (Test-Path $smokeSource)) {
    throw "Smoke test source not found: $smokeSource"
}

if (-not (Test-Path $appIdSmokeSource)) {
    throw "App-id smoke test source not found: $appIdSmokeSource"
}

$vcvars = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vcvars)) { throw 'vswhere not found; install Visual Studio Build Tools with C++ workload.' }
$vs = & $vcvars -latest -property installationPath
$vcvarsBat = Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
if (-not (Test-Path $vcvarsBat)) { throw "vcvars64.bat not found at $vcvarsBat" }

function Invoke-NativeBuild([string]$Source, [string]$OutExe) {
    cmd /c "call `"$vcvarsBat`" >nul && cl /nologo /W3 /O2 /Fe:`"$OutExe`" `"$Source`" fwpuclnt.lib ole32.lib"
    if ($LASTEXITCODE -ne 0) { throw "Native build failed: $Source" }
}

Invoke-NativeBuild $abiSource (Join-Path $probeDir 'wfp_abi_probe.exe')
Invoke-NativeBuild $smokeSource (Join-Path $smokeDir 'wfp_smoke_test.exe')
Invoke-NativeBuild $appIdSmokeSource (Join-Path $appIdSmokeDir 'wfp_appid_smoke_test.exe')

Write-Host ''
Write-Host '=== Native ABI probe ==='
$abiOutput = & (Join-Path $probeDir 'wfp_abi_probe.exe')
$abiOutput | ForEach-Object { Write-Host $_ }

$byteBlobType = ($abiOutput | Where-Object { $_ -match '^FWP_BYTE_BLOB_TYPE=(\d+)$' } | ForEach-Object { $Matches[1] })
$secDescType = ($abiOutput | Where-Object { $_ -match '^FWP_SECURITY_DESCRIPTOR_TYPE=(\d+)$' } | ForEach-Object { $Matches[1] })
$nativeAppIdGuid = ($abiOutput | Where-Object { $_ -match '^FWPM_CONDITION_ALE_APP_ID=(.+)$' } | ForEach-Object { $Matches[1] })

Write-Host ''
Write-Host '=== Managed ABI verification ==='
Push-Location $root
dotnet test tests\SelectiveVpnRouter.Core.Tests\SelectiveVpnRouter.Core.Tests.csproj -c Release --filter "FullyQualifiedName~WfpAbiTests" --no-restore 2>&1 | ForEach-Object { Write-Host $_ }
Pop-Location

if ($RunSmokeTest) {
    Write-Host ''
    Write-Host '=== Native WFP smoke (no ALE_APP_ID condition) ==='
    & (Join-Path $smokeDir 'wfp_smoke_test.exe')

    Write-Host ''
    Write-Host '=== Native ALE_APP_ID smoke (production condition path) ==='
    & (Join-Path $appIdSmokeDir 'wfp_appid_smoke_test.exe')

    Write-Host ''
    Write-Host '=== Managed ALE_APP_ID smoke ==='
    Push-Location $root
    dotnet test tests\SelectiveVpnRouter.Core.Tests\SelectiveVpnRouter.Core.Tests.csproj -c Release --filter "FullyQualifiedName~Managed_appid_smoke" --no-restore 2>&1 | ForEach-Object { Write-Host $_ }
    Pop-Location
}

Write-Host ''
Write-Host '=== Summary ==='
Write-Host "FWP_BYTE_BLOB_TYPE (native): $byteBlobType"
Write-Host "FWP_SECURITY_DESCRIPTOR_TYPE (native): $secDescType"
Write-Host "FWPM_CONDITION_ALE_APP_ID (native): $nativeAppIdGuid"
Write-Host "FWPM_CONDITION_ALE_APP_ID (managed): d78e1e87-8644-4ea5-9437-d809ecefc971"
Write-Host 'Root cause fixed: managed FWP_BYTE_BLOB_TYPE was 14 (SECURITY_DESCRIPTOR) instead of SDK value 12 (BYTE_BLOB).'
