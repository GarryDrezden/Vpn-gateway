# Refresh Windows Explorer icon cache (dev helper after icon change).
$ErrorActionPreference = "Stop"
Write-Host "Refreshing Explorer icon cache..."
Get-ChildItem "$env:LOCALAPPDATA\Microsoft\Windows\Explorer" -Filter "iconcache*.db" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1
Start-Process explorer.exe
Write-Host "Done. Press F5 in Explorer folder view if icon still looks old."