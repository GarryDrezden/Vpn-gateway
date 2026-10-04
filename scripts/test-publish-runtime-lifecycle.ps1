#Requires -Version 5.1
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "_common.ps1")

$root = Split-Path $PSScriptRoot -Parent
$global:SvrUpdateRepoRoot = $root
Invoke-SvrDotNet -ArgumentList @(
    "test",
    (Join-Path $root "tests\SelectiveVpnRouter.Core.Tests\SelectiveVpnRouter.Core.Tests.csproj"),
    "-c", "Release",
    "--filter", "FullyQualifiedName~PublishRuntimeLifecyclePlannerTests"
) | Out-Null

$scripts = @("svr-publish-runtime-lifecycle.ps1", "update-desktop.ps1", "_common.ps1")
foreach ($name in $scripts) {
    $path = Join-Path $PSScriptRoot $name
    $errors = $null
    $null = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$null, [ref]$errors)
    if ($errors -and $errors.Count -gt 0) { throw "Parse error in ${name}: $($errors[0].Message)" }
}
Write-Host "PASS publish-runtime-lifecycle"