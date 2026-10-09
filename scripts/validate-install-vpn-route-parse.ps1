#Requires -Version 5.1
# Read-only: Windows PowerShell 5.1 parser check for Slice 10A installer scripts.
$ErrorActionPreference = 'Stop'

$targets = @(
    (Join-Path $PSScriptRoot 'install-vpn-route.ps1')
    (Join-Path $PSScriptRoot '_install-vpn-route-helpers.ps1')
)

foreach ($path in $targets) {
    if (-not (Test-Path -LiteralPath $path)) {
        Write-Error "Missing file: $path"
        exit 1
    }
    $parseErrors = $null
    $null = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$null, [ref]$parseErrors)
    if ($parseErrors -and $parseErrors.Count -gt 0) {
        Write-Host "PARSE FAIL: $path" -ForegroundColor Red
        foreach ($err in $parseErrors) {
            Write-Host $err.ToString()
        }
        exit 1
    }
    Write-Host "PASS  parse  $path"
}

exit 0
