param(
    [Parameter(Mandatory = $true)][string]$PublishDir,
    [switch]$Quiet
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "_common.ps1")

$publishDir = (Resolve-Path -LiteralPath $PublishDir).Path
$appExe = Join-Path $publishDir "SelectiveVpnRouter.App.exe"
if (-not (Test-Path -LiteralPath $appExe)) {
    throw "app-publish-smoke: SelectiveVpnRouter.App.exe not found in $publishDir"
}

Test-SvrPublishRuntimeLayout -PublishDir $publishDir

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $appExe
$psi.WorkingDirectory = $publishDir
$psi.UseShellExecute = $false
$proc = [System.Diagnostics.Process]::Start($psi)
if ($null -eq $proc) {
    throw "app-publish-smoke: failed to start App process"
}

try {
    if (-not $proc.WaitForExit(5000)) {
        if (-not $Quiet) { Write-Host "app-publish-smoke: App still running after 5s (PASS)" }
        return
    }

    throw "app-publish-smoke: App exited early with code $($proc.ExitCode)"
}
finally {
    if (-not $proc.HasExited) {
        try { $proc.CloseMainWindow() | Out-Null } catch { }
        Start-Sleep -Milliseconds 500
        if (-not $proc.HasExited) {
            try { $proc.Kill() } catch { }
        }
    }
    $proc.Dispose()
}