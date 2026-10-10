# Reads VPN Route product version from Directory.Build.props (gateway SSOT).

function Get-VpnRouteVersionPropsPath {
    param([Parameter(Mandatory = $true)][string]$GatewayRoot)
    Join-Path $GatewayRoot 'Directory.Build.props'
}

function Get-VpnRouteProductVersionModel {
    param([Parameter(Mandatory = $true)][string]$GatewayRoot)

    $path = Get-VpnRouteVersionPropsPath -GatewayRoot $GatewayRoot
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing Directory.Build.props: $path"
    }

    [xml]$xml = Get-Content -LiteralPath $path
    $pg = $xml.Project.PropertyGroup | Select-Object -First 1
    $productVersion = [string]$pg.VpnRouteProductVersion
    if ([string]::IsNullOrWhiteSpace($productVersion)) { $productVersion = [string]$pg.Version }
    $channel = [string]$pg.VpnRouteReleaseChannel
    $revisionText = [string]$pg.VpnRouteReleaseRevision
    $revision = 0
    if ($revisionText -match '^\d+$') { $revision = [int]$revisionText }

    $display = $productVersion
    if ($revision -gt 0 -and -not [string]::IsNullOrWhiteSpace($channel) -and $channel -ne 'stable') {
        $display = "$productVersion $channel$revision"
    }

    $numeric = if ($revision -gt 0) { "$productVersion.$revision" } else { "$productVersion.0" }

    return [pscustomobject]@{
        ProductVersion   = $productVersion
        ReleaseChannel   = $channel
        ReleaseRevision  = $revision
        DisplayVersion   = $display
        NumericVersion   = $numeric
    }
}
