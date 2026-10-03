#Requires -Version 5.1
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "_common.ps1")
$root = Get-SvrRepoRoot

Write-Host "PORTABLE PREFLIGHT"
$launcherPath = Join-Path $root "src\SelectiveVpnRouter.Core\Portable\PortableBootstrapProcessLauncher.cs"
$launcherSrc = Get-Content $launcherPath -Raw
if ($launcherSrc -notmatch 'Verb\s*=\s*"runas"' -or $launcherSrc -notmatch 'UseShellExecute\s*=\s*true') {
    throw "Portable bootstrap launcher must use ShellExecute runas elevation"
}

$props = [xml](Get-Content (Join-Path $root "Directory.Build.props"))
$version = $props.Project.PropertyGroup.Version | Select-Object -First 1
$stage = Join-Path $root "artifacts\portable\VPN-Route-$version-x64"
$zip = "$stage.zip"

if (-not (Test-Path $stage)) {
    & (Join-Path $PSScriptRoot "build-portable.ps1")
    if ($LASTEXITCODE -ne 0) { throw "build-portable failed" }
}

$required = @(
    "SelectiveVpnRouter.App.exe",
    "SelectiveVpnRouter.Service.exe",
    "SelectiveVpnRouter.Bootstrap.exe",
    "SelectiveVpnRouter.Core.dll",
    "portable-manifest.json",
    "README.txt",
    "driver\SelectiveVpnCallout.sys",
    "driver\SelectiveVpnCallout.inf"
)
foreach ($r in $required) {
    if (-not (Test-Path (Join-Path $stage $r))) { throw "Missing $r" }
}

$manifest = Get-Content (Join-Path $stage "portable-manifest.json") -Raw | ConvertFrom-Json
if ($manifest.productVersion -ne $version) { throw "manifest productVersion mismatch" }
if ($manifest.packageFormatVersion -ne 1) { throw "manifest format mismatch" }

$forbidden = @("config.json", "vpn-valpolyakov.ovpn", "p2.py")
foreach ($f in $forbidden) {
    if (Get-ChildItem -Path $stage -Recurse -Filter $f -ErrorAction SilentlyContinue) {
        throw "Forbidden file in package: $f"
    }
}

if (Get-ChildItem -Path $stage -Recurse -Include "patch_*.py","fix_*.py","*.iss" -ErrorAction SilentlyContinue) {
    throw "Forbidden patch/installer artifacts in package"
}

$prev = $env:VPN_ROUTE_ENABLE_WORK_VPN
Remove-Item Env:VPN_ROUTE_ENABLE_WORK_VPN -ErrorAction SilentlyContinue
try {
    dotnet test (Join-Path $root "tests\SelectiveVpnRouter.Core.Tests\SelectiveVpnRouter.Core.Tests.csproj") -c Release --filter "FullyQualifiedName~PortableBootstrap|FullyQualifiedName~FeatureFlags" --no-build 2>$null
    if ($LASTEXITCODE -ne 0) {
        dotnet test (Join-Path $root "tests\SelectiveVpnRouter.Core.Tests\SelectiveVpnRouter.Core.Tests.csproj") -c Release --filter "FullyQualifiedName~PortableBootstrap|FullyQualifiedName~FeatureFlags" | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "feature/bootstrap tests failed" }
    }
} finally {
    if ($null -eq $prev) { Remove-Item Env:VPN_ROUTE_ENABLE_WORK_VPN -ErrorAction SilentlyContinue }
    else { $env:VPN_ROUTE_ENABLE_WORK_VPN = $prev }
}

if (-not (Test-Path $zip)) { throw "ZIP missing: $zip" }

$statusJson = & (Join-Path $stage "SelectiveVpnRouter.Bootstrap.exe") status --root $stage
if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne 1) { throw "bootstrap status failed exit=$LASTEXITCODE" }

Write-Host "PORTABLE PREFLIGHT: PASS"
exit 0
