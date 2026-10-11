#Requires -Version 5.1
# Regression: Windows app icon pipeline must not mutate tracked branding/source paths.
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
. (Join-Path $repoRoot 'scripts\branding-asset-paths.ps1')

$paths = Get-SvrBrandingAssetPaths -RepoRoot $repoRoot
$generateScript = Join-Path $repoRoot 'scripts\generate-app-icon.ps1'
$publishScript = Join-Path $repoRoot 'scripts\publish-desktop.ps1'
$updateScript = Join-Path $repoRoot 'scripts\update-desktop.ps1'
$appProject = Join-Path $repoRoot 'src\SelectiveVpnRouter.App\SelectiveVpnRouter.App.csproj'

function Assert-FileAbsent {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$Label)
    if (Test-Path -LiteralPath $Path) {
        throw "$Label must not exist as a tracked/generated duplicate: $Path"
    }
}

function Get-GitPorcelain {
    param([string[]]$PathSpecs)
    Push-Location $repoRoot
    try {
        if ($PathSpecs -and $PathSpecs.Count -gt 0) {
            return (& git status --porcelain -- @PathSpecs 2>$null)
        }
        return (& git status --porcelain 2>$null)
    }
    finally {
        Pop-Location
    }
}

function Assert-TrackedBrandingClean {
    param([string]$Phase)
    $specs = @(
        'assets/branding/vpn-route-icon-source.png'
        'assets/branding/vpn-route-lockup.png'
        'assets/branding/vpn-route-wordmark.png'
    )
    $dirty = Get-GitPorcelain -PathSpecs $specs
    if ($dirty) {
        throw "Tracked branding sources modified during ${Phase}:`n$dirty"
    }
}

foreach ($legacyRel in @(
        'assets/branding/vpn-route-icon.ico'
        'assets/branding/vpn-route-icon-tight-256.png'
        'src/SelectiveVpnRouter.App/vpn-route-icon.ico'
    )) {
    $legacyPath = Join-Path $repoRoot ($legacyRel -replace '/', '\')
    Assert-FileAbsent -Path $legacyPath -Label 'legacy generated icon'
}

# 1) No required generated ICO under src
Assert-FileAbsent -Path (Join-Path $repoRoot 'src\SelectiveVpnRouter.App\vpn-route-icon.ico') -Label 'src app ico'

# 2) Static contract: scripts must not reference legacy tracked ICO paths as outputs
$publishText = Get-Content -LiteralPath $publishScript -Raw
if ($publishText -match 'assets\\branding\\vpn-route-icon\.ico' -or $publishText -match 'SelectiveVpnRouter\.App\\vpn-route-icon\.ico') {
    throw 'publish-desktop.ps1 still references legacy tracked ICO paths'
}
$updateText = Get-Content -LiteralPath $updateScript -Raw
if ($updateText -match 'assets\\branding\\vpn-route-icon\.ico' -or $updateText -match 'SelectiveVpnRouter\.App\\vpn-route-icon\.ico') {
    throw 'update-desktop.ps1 still references legacy tracked ICO paths'
}

# 3) Generator writes only to allowed generated paths; deterministic hash
$tempOut = Join-Path $env:TEMP ('vpnroute-icon-regression-' + [Guid]::NewGuid().ToString('n'))
New-Item -ItemType Directory -Path $tempOut -Force | Out-Null
try {
    $tempIco = Join-Path $tempOut 'test.ico'
    $tempPreview = Join-Path $tempOut 'test-preview.png'
    & $generateScript -SourcePng $paths.SourcePng -OutputIco $tempIco -PreviewPng $tempPreview -Quiet
    if (-not $?) { throw 'generate-app-icon.ps1 failed' }
    if (-not (Test-Path -LiteralPath $tempIco)) { throw 'Generator did not create ICO' }
    $hash1 = (Get-FileHash -LiteralPath $tempIco -Algorithm SHA256).Hash
    & $generateScript -SourcePng $paths.SourcePng -OutputIco $tempIco -PreviewPng $tempPreview -Quiet
    $hash2 = (Get-FileHash -LiteralPath $tempIco -Algorithm SHA256).Hash
    if ($hash1 -ne $hash2) {
        throw "Generator output not deterministic: $hash1 vs $hash2"
    }
}
finally {
    Remove-Item -LiteralPath $tempOut -Recurse -Force -ErrorAction SilentlyContinue
}

Assert-TrackedBrandingClean -Phase 'generator temp run'

# SkipIfUpToDate: A–E (isolated temp outputs; script freshness via mtime only — git uses content)
$skipOut = Join-Path $env:TEMP ('vpnroute-icon-skip-' + [Guid]::NewGuid().ToString('n'))
New-Item -ItemType Directory -Path $skipOut -Force | Out-Null
$skipIco = Join-Path $skipOut 'out.ico'
$skipPreview = Join-Path $skipOut 'out-preview.png'
$genScriptPath = Join-Path $repoRoot 'scripts\generate-app-icon.ps1'
$pathsScriptPath = Join-Path $repoRoot 'scripts\branding-asset-paths.ps1'
try {
    if (Test-Path -LiteralPath $skipIco) { Remove-Item -LiteralPath $skipIco -Force }
    if (Test-Path -LiteralPath $skipPreview) { Remove-Item -LiteralPath $skipPreview -Force }
    & $generateScript -SourcePng $paths.SourcePng -OutputIco $skipIco -PreviewPng $skipPreview -SkipIfUpToDate -Quiet
    if (-not $?) { throw 'SkipIfUpToDate first run failed' }
    if (-not (Test-Path -LiteralPath $skipIco) -or -not (Test-Path -LiteralPath $skipPreview)) {
        throw 'SkipIfUpToDate first run did not create both outputs (A)'
    }
    $icoAfterFirst = (Get-Item -LiteralPath $skipIco).LastWriteTimeUtc
    $previewAfterFirst = (Get-Item -LiteralPath $skipPreview).LastWriteTimeUtc
    Start-Sleep -Seconds 2
    & $generateScript -SourcePng $paths.SourcePng -OutputIco $skipIco -PreviewPng $skipPreview -SkipIfUpToDate -Quiet
    if (-not $?) { throw 'SkipIfUpToDate second run failed' }
    if ((Get-Item -LiteralPath $skipIco).LastWriteTimeUtc -ne $icoAfterFirst -or (Get-Item -LiteralPath $skipPreview).LastWriteTimeUtc -ne $previewAfterFirst) {
        throw 'SkipIfUpToDate second run should not rewrite fresh outputs (B)'
    }
    $genItem = Get-Item -LiteralPath $genScriptPath
    $genSaved = $genItem.LastWriteTimeUtc
    try {
        $genItem.LastWriteTimeUtc = $icoAfterFirst.AddSeconds(5)
        Start-Sleep -Seconds 2
        & $generateScript -SourcePng $paths.SourcePng -OutputIco $skipIco -PreviewPng $skipPreview -SkipIfUpToDate -Quiet
        if (-not $?) { throw 'SkipIfUpToDate run after generator touch failed' }
        if ((Get-Item -LiteralPath $skipIco).LastWriteTimeUtc -le $icoAfterFirst) {
            throw 'Generator script touch must invalidate ICO output (C)'
        }
    }
    finally {
        $genItem.LastWriteTimeUtc = $genSaved
    }
    $icoAfterC = (Get-Item -LiteralPath $skipIco).LastWriteTimeUtc
    $pathsItem = Get-Item -LiteralPath $pathsScriptPath
    $pathsSaved = $pathsItem.LastWriteTimeUtc
    try {
        $pathsItem.LastWriteTimeUtc = $icoAfterC.AddSeconds(5)
        Start-Sleep -Seconds 2
        & $generateScript -SourcePng $paths.SourcePng -OutputIco $skipIco -PreviewPng $skipPreview -SkipIfUpToDate -Quiet
        if (-not $?) { throw 'SkipIfUpToDate run after paths touch failed' }
        if ((Get-Item -LiteralPath $skipIco).LastWriteTimeUtc -le $icoAfterC) {
            throw 'branding-asset-paths.ps1 touch must invalidate ICO output (D)'
        }
    }
    finally {
        $pathsItem.LastWriteTimeUtc = $pathsSaved
    }
    Remove-Item -LiteralPath $skipPreview -Force
    & $generateScript -SourcePng $paths.SourcePng -OutputIco $skipIco -PreviewPng $skipPreview -SkipIfUpToDate -Quiet
    if (-not $?) { throw 'SkipIfUpToDate run after preview delete failed' }
    if (-not (Test-Path -LiteralPath $skipPreview)) {
        throw 'Missing preview must force regeneration (E)'
    }
}
finally {
    Remove-Item -LiteralPath $skipOut -Recurse -Force -ErrorAction SilentlyContinue
}

Assert-TrackedBrandingClean -Phase 'SkipIfUpToDate checks'

function Assert-LegacyIconGitClean {
    $legacySpecs = @(
        'assets/branding/vpn-route-icon.ico'
        'assets/branding/vpn-route-icon-tight-256.png'
        'src/SelectiveVpnRouter.App/vpn-route-icon.ico'
    )
    $lines = Get-GitPorcelain -PathSpecs $legacySpecs
    foreach ($line in ($lines | Where-Object { $_ -match '\S' })) {
        if ($line -match '^\?\?' -or $line -match '^ M' -or $line -match '^M ') {
            throw "Legacy icon path must not be modified/untracked on disk (F): $line"
        }
    }
}

Assert-LegacyIconGitClean

# 4) Clean build generates linked icon in intermediate output
Push-Location $repoRoot
try {
    & dotnet build $appProject -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "dotnet build App failed with exit code $LASTEXITCODE" }
}
finally {
    Pop-Location
}

Assert-TrackedBrandingClean -Phase 'dotnet build App'

$generatedIco = $paths.GeneratedIco
if (-not (Test-Path -LiteralPath $generatedIco)) {
    throw "Expected generated ICO at $generatedIco after build"
}

# 5) Isolated publish produces sidecar + exe embed path (no live publish)
$staging = Join-Path $repoRoot ('artifacts\publish\icon-pipeline-regression-' + [Guid]::NewGuid().ToString('n'))
try {
    & $publishScript -OutputDirectory $staging -Quiet
    if (-not $?) { throw 'publish-desktop.ps1 failed' }
    $appExe = Join-Path $staging 'SelectiveVpnRouter.App.exe'
    $sidecar = Join-Path $staging 'vpn-route-icon.ico'
    if (-not (Test-Path -LiteralPath $appExe)) { throw 'Published App exe missing' }
    if (-not (Test-Path -LiteralPath $sidecar)) { throw 'Published sidecar vpn-route-icon.ico missing' }
}
finally {
    Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
}

Assert-TrackedBrandingClean -Phase 'isolated publish-desktop'
Assert-LegacyIconGitClean

Write-Output 'PASS app-icon-pipeline-regression'
exit 0
