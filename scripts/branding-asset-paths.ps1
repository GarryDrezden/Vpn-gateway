# Canonical VPN Route Windows app branding paths (repo-relative, no CWD assumptions).
$ErrorActionPreference = "Stop"

function Get-SvrBrandingRepoRoot {
    param(
        [string]$FromScriptRoot = $PSScriptRoot
    )
    if ([string]::IsNullOrWhiteSpace($FromScriptRoot)) {
        throw "Get-SvrBrandingRepoRoot requires a script root (PSScriptRoot)."
    }
    return (Resolve-Path (Join-Path $FromScriptRoot "..")).Path
}

function Get-SvrBrandingAssetPaths {
    param(
        [string]$RepoRoot = (Get-SvrBrandingRepoRoot)
    )
    $generatedDir = Join-Path $RepoRoot "artifacts\branding"
    [pscustomobject]@{
        RepoRoot     = $RepoRoot
        SourcePng    = Join-Path $RepoRoot "assets\branding\vpn-route-icon-source.png"
        GeneratedDir = $generatedDir
        GeneratedIco = Join-Path $generatedDir "vpn-route-icon.ico"
        PreviewPng   = Join-Path $generatedDir "vpn-route-icon-tight-256.png"
    }
}

# Files whose timestamps must be reflected in generated ICO/preview freshness.
function Get-SvrBrandingGenerationInputPaths {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$SourcePng
    )
    @(
        $SourcePng
        (Join-Path $RepoRoot "scripts\generate-app-icon.ps1")
        (Join-Path $RepoRoot "scripts\branding-asset-paths.ps1")
    )
}

function Get-SvrBrandingGenerationInputMaxTimeUtc {
    param(
        [Parameter(Mandatory = $true)][string[]]$InputPaths
    )
    $max = [DateTime]::MinValue
    foreach ($path in $InputPaths) {
        if (-not (Test-Path -LiteralPath $path)) {
            return $null
        }
        $time = (Get-Item -LiteralPath $path).LastWriteTimeUtc
        if ($time -gt $max) {
            $max = $time
        }
    }
    return $max
}

function Test-SvrBrandingGeneratedOutputsCurrent {
    param(
        [Parameter(Mandatory = $true)][string]$OutputIco,
        [Parameter(Mandatory = $true)][string]$PreviewPng,
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$SourcePng
    )
    if (-not (Test-Path -LiteralPath $OutputIco) -or -not (Test-Path -LiteralPath $PreviewPng)) {
        return $false
    }
    $maxInput = Get-SvrBrandingGenerationInputMaxTimeUtc -InputPaths (
        Get-SvrBrandingGenerationInputPaths -RepoRoot $RepoRoot -SourcePng $SourcePng
    )
    if ($null -eq $maxInput) {
        return $false
    }
    $icoTime = (Get-Item -LiteralPath $OutputIco).LastWriteTimeUtc
    $previewTime = (Get-Item -LiteralPath $PreviewPng).LastWriteTimeUtc
    return ($icoTime -ge $maxInput -and $previewTime -ge $maxInput)
}

# Tracked paths that must never be written by generate/publish (regression guard).
function Get-SvrBrandingForbiddenWritePaths {
    param(
        [string]$RepoRoot = (Get-SvrBrandingRepoRoot)
    )
    @(
        (Join-Path $RepoRoot "assets\branding\vpn-route-icon.ico")
        (Join-Path $RepoRoot "assets\branding\vpn-route-icon-tight-256.png")
        (Join-Path $RepoRoot "src\SelectiveVpnRouter.App\vpn-route-icon.ico")
    )
}
