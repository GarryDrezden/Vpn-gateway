# Shared driver + manifest layout for dev publish and portable ZIP builds.

function Ensure-SvrPortableDriverLayout {
    param(
        [Parameter(Mandatory = $true)][string]$LayoutRoot,
        [Parameter(Mandatory = $true)][string]$RepoRoot
    )

    $driverDir = Join-Path $LayoutRoot "driver"
    New-Item -ItemType Directory -Path $driverDir -Force | Out-Null

    $sysSrc = Get-SvrDriverStagingSysPath -Root $RepoRoot -Configuration Release
    if (-not (Test-Path -LiteralPath $sysSrc)) {
        $buildDriver = Join-Path $PSScriptRoot "build-driver.ps1"
        Write-SvrUpdateDetail "Driver sys missing; running build-driver.ps1"
        & $buildDriver
        if ($LASTEXITCODE -ne 0) { throw "build-driver failed" }
    }

    if (-not (Test-Path -LiteralPath $sysSrc)) {
        throw "Driver sys not found after build: $sysSrc"
    }

    Copy-Item -LiteralPath $sysSrc -Destination (Join-Path $driverDir "SelectiveVpnCallout.sys") -Force
    $infSrc = Join-Path $RepoRoot "driver\SelectiveVpnCallout\SelectiveVpnCallout.inf"
    if (-not (Test-Path -LiteralPath $infSrc)) {
        throw "Driver inf not found: $infSrc"
    }

    Copy-Item -LiteralPath $infSrc -Destination (Join-Path $driverDir "SelectiveVpnCallout.inf") -Force
    $catSrc = Join-Path $RepoRoot "artifacts\driver\staging\Release\SelectiveVpnCallout.cat"
    if (Test-Path -LiteralPath $catSrc) {
        Copy-Item -LiteralPath $catSrc -Destination (Join-Path $driverDir "SelectiveVpnCallout.cat") -Force
    }
}

function Write-SvrPortableManifestForLayout {
    param(
        [Parameter(Mandatory = $true)][string]$LayoutRoot,
        [Parameter(Mandatory = $true)][string]$RepoRoot
    )

    $props = [xml](Get-Content (Join-Path $RepoRoot "Directory.Build.props"))
    $version = $props.Project.PropertyGroup.Version | Select-Object -First 1
    if (-not $version) { $version = "0.0.0" }

    $commit = "unknown"
    try {
        Push-Location $RepoRoot
        $commit = (git rev-parse --short HEAD 2>$null)
        if ($LASTEXITCODE -ne 0) { $commit = "unknown" }
    }
    finally { Pop-Location }

    $infSrc = Join-Path $RepoRoot "driver\SelectiveVpnCallout\SelectiveVpnCallout.inf"
    $driverVer = "unknown"
    if (Test-Path -LiteralPath $infSrc) {
        $line = Select-String -Path $infSrc -Pattern "^DriverVer\s*=" | Select-Object -First 1
        if ($line) { $driverVer = ($line.Line -replace "^DriverVer\s*=\s*", "").Trim() }
    }

    $serviceExe = Join-Path $LayoutRoot "SelectiveVpnRouter.Service.exe"
    $coreDll = Join-Path $LayoutRoot "SelectiveVpnRouter.Core.dll"
    $serviceVer = if (Test-Path -LiteralPath $serviceExe) { (Get-Item -LiteralPath $serviceExe).VersionInfo.FileVersion } else { $null }
    $coreVer = if (Test-Path -LiteralPath $coreDll) { (Get-Item -LiteralPath $coreDll).VersionInfo.FileVersion } else { $null }

    $manifest = [ordered]@{
        product = "VPN Route"
        productVersion = $version
        architecture = "x64"
        buildCommit = $commit
        serviceVersion = $serviceVer
        coreVersion = $coreVer
        driverVersion = $driverVer
        packageFormatVersion = 1
    }
    ($manifest | ConvertTo-Json -Depth 3) | Set-Content -Path (Join-Path $LayoutRoot "portable-manifest.json") -Encoding UTF8
}