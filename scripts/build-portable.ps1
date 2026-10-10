#Requires -Version 5.1
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "_common.ps1")

$root = Get-SvrRepoRoot
. (Join-Path $PSScriptRoot "_product-version.ps1")
$productModel = Get-VpnRouteProductVersionModel -GatewayRoot $root
$version = $productModel.ProductVersion
if (-not $version) { $version = "0.0.0" }

$commit = ""
try {
    Push-Location $root
    $commit = (git rev-parse --short HEAD 2>$null)
    if ($LASTEXITCODE -ne 0) { $commit = "unknown" }
} finally { Pop-Location }

$stageName = "VPN-Route-$version-x64"
$stage = Join-Path $root "artifacts\portable\$stageName"
$zipPath = "$stage.zip"

Write-Host "PORTABLE BUILD"
Write-Host "product=$($productModel.DisplayVersion) numeric=$($productModel.NumericVersion) commit=$commit"

if (Test-Path $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null

Write-Host "=== tests ==="
dotnet test (Join-Path $root "tests\SelectiveVpnRouter.Core.Tests\SelectiveVpnRouter.Core.Tests.csproj") -c Release | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Core tests failed" }
dotnet test (Join-Path $root "tests\SelectiveVpnRouter.Proxy.Tests\SelectiveVpnRouter.Proxy.Tests.csproj") -c Release | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Proxy tests failed" }

$pubArgs = @("publish", "-c", "Release", "-r", "win-x64", "--self-contained", "true", "/p:PublishSingleFile=false", "-o", $stage)
$projects = @(
    "src\SelectiveVpnRouter.Service\SelectiveVpnRouter.Service.csproj",
    "src\SelectiveVpnRouter.App\SelectiveVpnRouter.App.csproj",
    "src\SelectiveVpnRouter.Probe\SelectiveVpnRouter.Probe.csproj",
    "src\SelectiveVpnRouter.Bootstrap\SelectiveVpnRouter.Bootstrap.csproj"
)
foreach ($proj in $projects) {
    Write-Host "publish $proj"
    dotnet @($pubArgs + (Join-Path $root $proj))
    if ($LASTEXITCODE -ne 0) { throw "publish failed: $proj" }
}

$driverDir = Join-Path $stage "driver"
New-Item -ItemType Directory -Path $driverDir -Force | Out-Null
$sysSrc = Get-SvrDriverStagingSysPath -Root $root -Configuration Release
if (-not (Test-Path $sysSrc)) {
    Write-Host "driver sys missing; running build-driver.ps1"
    & (Join-Path $PSScriptRoot "build-driver.ps1")
    if ($LASTEXITCODE -ne 0) { throw "build-driver failed" }
}
Copy-Item -LiteralPath $sysSrc -Destination (Join-Path $driverDir "SelectiveVpnCallout.sys") -Force
$infSrc = Join-Path $root "driver\SelectiveVpnCallout\SelectiveVpnCallout.inf"
Copy-Item -LiteralPath $infSrc -Destination (Join-Path $driverDir "SelectiveVpnCallout.inf") -Force
$catSrc = Join-Path $root "artifacts\driver\staging\Release\SelectiveVpnCallout.cat"
if (Test-Path $catSrc) {
    Copy-Item -LiteralPath $catSrc -Destination (Join-Path $driverDir "SelectiveVpnCallout.cat") -Force
}

$driverVer = "unknown"
if (Test-Path $infSrc) {
    $line = Select-String -Path $infSrc -Pattern "^DriverVer\s*=" | Select-Object -First 1
    if ($line) { $driverVer = ($line.Line -replace "^DriverVer\s*=\s*", "").Trim() }
}

$serviceVer = (Get-Item (Join-Path $stage "SelectiveVpnRouter.Service.exe")).VersionInfo.FileVersion
$coreVer = (Get-Item (Join-Path $stage "SelectiveVpnRouter.Core.dll")).VersionInfo.FileVersion
$manifest = [ordered]@{
    product = "VPN Route"
    productVersion = $version
    displayVersion = $productModel.DisplayVersion
    releaseChannel = $productModel.ReleaseChannel
    releaseRevision = $productModel.ReleaseRevision
    fileVersion = $productModel.NumericVersion
    architecture = "x64"
    buildCommit = $commit
    serviceVersion = $serviceVer
    coreVersion = $coreVer
    driverVersion = $driverVer
    packageFormatVersion = 1
}
($manifest | ConvertTo-Json -Depth 3) | Set-Content -Path (Join-Path $stage "portable-manifest.json") -Encoding UTF8

$readme = @"
VPN Route portable package ($($productModel.DisplayVersion))

1. Extract this folder anywhere local (not a UNC path).
2. Run SelectiveVpnRouter.App.exe
3. On first launch approve UAC once to register the Windows service and product driver.
4. Install OpenVPN Community separately and set openvpn.exe in Settings before connecting.

User settings remain in %ProgramData%\SelectiveVpnRouter
"@
Set-Content -Path (Join-Path $stage "README.txt") -Value $readme -Encoding UTF8

if (Test-Path $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $zipPath)

Write-Host "PORTABLE BUILD: PASS"
Write-Host "artifact=$zipPath"
Write-Host "sizeMB=$([math]::Round((Get-Item $zipPath).Length / 1MB, 2))"
