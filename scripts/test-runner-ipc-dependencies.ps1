$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "_update-helpers.ps1")
Assert-SvrRunnerIpcDependencies
if (-not (Get-Command Invoke-SvrIpc -ErrorAction SilentlyContinue)) { throw "missing Invoke-SvrIpc" }
Write-Host "PASS test-runner-ipc-dependencies"
exit 0