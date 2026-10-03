$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
Push-Location $Root
try {
  Write-Host "DUAL VPN PREFLIGHT"
  $required = @(
    "src/SelectiveVpnRouter.Core/WorkVpnModels.cs",
    "src/SelectiveVpnRouter.Core/WorkVpnAuthFileGuard.cs",
    "src/SelectiveVpnRouter.Core/WorkVpnAdapterMatcher.cs",
    "src/SelectiveVpnRouter.Network/WorkOpenVpnController.cs",
    "src/SelectiveVpnRouter.Service/RouterEngine.WorkVpn.cs",
    "docs/work-vpn-dual-vpn.md"
  )
  foreach ($r in $required) {
    if (-not (Test-Path (Join-Path $Root $r))) { throw "Missing $r" }
  }
  $prev = $env:VPN_ROUTE_ENABLE_WORK_VPN
  $env:VPN_ROUTE_ENABLE_WORK_VPN = "1"
  try {
    dotnet test tests/SelectiveVpnRouter.Core.Tests/SelectiveVpnRouter.Core.Tests.csproj -c Release --filter "FullyQualifiedName~WorkVpn" | Out-Host
  } finally {
    if ($null -eq $prev) { Remove-Item Env:VPN_ROUTE_ENABLE_WORK_VPN -ErrorAction SilentlyContinue }
    else { $env:VPN_ROUTE_ENABLE_WORK_VPN = $prev }
  }
  if ($LASTEXITCODE -ne 0) { throw "Core WorkVpn tests failed" }
  dotnet test tests/SelectiveVpnRouter.Proxy.Tests/SelectiveVpnRouter.Proxy.Tests.csproj -c Release | Out-Host
  if ($LASTEXITCODE -ne 0) { throw "Proxy tests failed" }
  dotnet build src/SelectiveVpnRouter.Service/SelectiveVpnRouter.Service.csproj -c Release | Out-Host
  if ($LASTEXITCODE -ne 0) { throw "Build failed" }
  Write-Host "DUAL VPN PREFLIGHT: PASS"
  exit 0
}
catch {
  Write-Host "DUAL VPN PREFLIGHT: FAIL"
  Write-Host $_.Exception.Message
  exit 1
}
finally {
  Pop-Location
}