# Shared product service + callout driver lifecycle for publish tree replacement.

$script:SvrProductServiceName = "SelectiveVpnRouter"
$script:SvrDriverServiceName = "SelectiveVpnCallout"

function ConvertFrom-SvrKernelImagePath {
    param([string]$ImagePath)

    if ([string]::IsNullOrWhiteSpace($ImagePath)) { return $null }
    $trimmed = $ImagePath.Trim().Trim('"')
    if ($trimmed.StartsWith('\??\')) {
        $trimmed = $trimmed.Substring(4)
    }

    try {
        return [System.IO.Path]::GetFullPath($trimmed)
    }
    catch {
        return $null
    }
}

function Get-SvrServiceImagePathFromRegistry {
    param([Parameter(Mandatory = $true)][string]$ServiceName)

    try {
        $key = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
        if (-not (Test-Path -LiteralPath $key)) { return $null }
        $value = (Get-ItemProperty -LiteralPath $key -Name ImagePath -ErrorAction Stop).ImagePath
        if ([string]::IsNullOrWhiteSpace($value)) { return $null }
        return [string]$value
    }
    catch {
        return $null
    }
}

function Get-SvrPublishRuntimeSnapshot {
    $product = Get-Service -Name $script:SvrProductServiceName -ErrorAction SilentlyContinue
    $driver = Get-Service -Name $script:SvrDriverServiceName -ErrorAction SilentlyContinue

    $productPath = $null
    if ($product) {
        $wmi = Get-CimInstance Win32_Service -Filter "Name='$($script:SvrProductServiceName)'" -ErrorAction SilentlyContinue
        if ($wmi -and $wmi.PathName) {
            $productPath = ConvertFrom-SvrKernelImagePath -ImagePath $wmi.PathName
        }
    }

    $driverPath = $null
    if ($driver) {
        $driverPath = ConvertFrom-SvrKernelImagePath -ImagePath (Get-SvrServiceImagePathFromRegistry -ServiceName $script:SvrDriverServiceName)
    }

    return [pscustomobject]@{
        ProductInstalled  = ($null -ne $product)
        ProductWasRunning = ($product -and $product.Status -eq "Running")
        ProductImagePath  = $productPath
        DriverInstalled   = ($null -ne $driver)
        DriverWasRunning  = ($driver -and $driver.Status -eq "Running")
        DriverImagePath   = $driverPath
    }
}

function Write-SvrPublishRuntimeSnapshot {
    param($Snapshot)

    Write-SvrUpdateLogLine (
        "runtime-snapshot: product installed=$($Snapshot.ProductInstalled) wasRunning=$($Snapshot.ProductWasRunning) path=$($Snapshot.ProductImagePath)"
    )
    Write-SvrUpdateLogLine (
        "runtime-snapshot: driver installed=$($Snapshot.DriverInstalled) wasRunning=$($Snapshot.DriverWasRunning) path=$($Snapshot.DriverImagePath)"
    )
}

function Test-SvrShouldStopDriverForPublishSwap {
    param(
        $Snapshot,
        [Parameter(Mandatory = $true)][string]$PublishDir
    )

    return $Snapshot.DriverInstalled -and $Snapshot.DriverWasRunning -and
        (Test-SvrPathUnderPublishDirectory -Path $Snapshot.DriverImagePath -PublishDir $PublishDir)
}

function Test-SvrIsEphemeralDriverBuildPath {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    try { $full = [IO.Path]::GetFullPath($Path) } catch { return $false }
    $norm = $full -replace '/', '\'
    return ($norm -match '\\artifacts\\driver\\staging\\') -or
        ($norm -match '\\artifacts\\driver\\Release\\') -or
        ($norm -match '\\artifacts\\driver\\Debug\\') -or
        ($norm -match '\\artifacts\\driver\\rollback\\')
}

function Test-SvrShouldRepairDriverImagePathToPublishLayout {
    param(
        $Snapshot,
        [Parameter(Mandatory = $true)][string]$PublishDir
    )

    if (-not $Snapshot -or -not $Snapshot.DriverInstalled) {
        return $true
    }

    if (Test-SvrPathUnderPublishDirectory -Path $Snapshot.DriverImagePath -PublishDir $PublishDir) {
        return $true
    }

    return Test-SvrIsEphemeralDriverBuildPath -Path $Snapshot.DriverImagePath
}

function Test-SvrShouldStopDriverForDirectoryCleanup {
    param(
        [Parameter(Mandatory = $true)][string]$Directory,
        [string]$PublishParentDir
    )

    $driver = Get-Service -Name $script:SvrDriverServiceName -ErrorAction SilentlyContinue
    if (-not $driver -or $driver.Status -ne "Running") {
        return $false
    }

    $imagePath = ConvertFrom-SvrKernelImagePath -ImagePath (Get-SvrServiceImagePathFromRegistry -ServiceName $script:SvrDriverServiceName)
    if (Test-SvrPathUnderPublishDirectory -Path $imagePath -PublishDir $Directory) {
        return $true
    }

    $parent = if ($PublishParentDir) { $PublishParentDir } else { Split-Path $Directory -Parent }
    $canonicalLiveRoot = Join-Path $parent "SelectiveVpnRouter"
    if (Test-SvrPathUnderPublishDirectory -Path $imagePath -PublishDir $canonicalLiveRoot) {
        return $true
    }

    return $false
}

function Invoke-SvrSc {
    param(
        [Parameter(Mandatory = $true)][string]$ArgumentString,
        [switch]$IgnoreErrors
    )

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = "sc.exe"
    $psi.Arguments = $ArgumentString
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $p = [System.Diagnostics.Process]::Start($psi)
    $stdout = $p.StandardOutput.ReadToEnd()
    $stderr = $p.StandardError.ReadToEnd()
    $p.WaitForExit(30000)

    $result = [pscustomobject]@{
        ExitCode = $p.ExitCode
        Output   = ($stdout + $stderr).Trim()
    }

    if ($p.ExitCode -ne 0 -and -not $IgnoreErrors) {
        throw "sc.exe $ArgumentString failed (exit $($p.ExitCode)): $($result.Output)"
    }

    return $result
}

function Get-SvrTestSigningEnabled {
    try {
        $out = bcdedit /enum "{current}" 2>$null | Out-String
        return $out -match "testsigning\s+Yes"
    }
    catch {
        return $false
    }
}

function Get-SvrWindowsServiceStateDiagnostics {
    param(
        [Parameter(Mandatory = $true)][string]$ServiceName,
        [switch]$IncludeStartAttempt
    )

    $lines = New-Object System.Collections.Generic.List[string]
    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if (-not $svc) {
        $lines.Add("service=$ServiceName not installed")
        return ($lines -join "; ")
    }

    $regPath = Get-SvrServiceImagePathFromRegistry -ServiceName $ServiceName
    $lines.Add("state=$($svc.Status)")
    $lines.Add("ImagePath=$regPath")

    $wmi = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'" -ErrorAction SilentlyContinue
    if ($wmi) {
        $lines.Add("WIN32_EXIT_CODE=$($wmi.ExitCode)")
        $lines.Add("SERVICE_EXIT_CODE=$($wmi.ServiceSpecificExitCode)")
    }

    if ($IncludeStartAttempt) {
        $start = Invoke-SvrSc -ArgumentString "start $ServiceName" -IgnoreErrors
        $lines.Add("sc start exit=$($start.ExitCode) output=$($start.Output)")
        Start-Sleep -Milliseconds 500
        $svc.Refresh()
    }

    $lines.Add("stateAfterStart=$($svc.Status)")

    if ($ServiceName -eq $script:SvrDriverServiceName) {
        $sysPath = ConvertFrom-SvrKernelImagePath -ImagePath $regPath
        if ($sysPath -and (Test-Path -LiteralPath $sysPath)) {
            $sig = Get-AuthenticodeSignature -LiteralPath $sysPath
            $lines.Add("driverAuthenticode=$($sig.Status)")
            $lines.Add("driverExists=True")
        }
        else {
            $lines.Add("driverExists=False path=$sysPath")
        }

        $lines.Add("testsigning=$(Get-SvrTestSigningEnabled)")
    }

    try {
        $evt = Get-WinEvent -FilterHashtable @{
            LogName   = "System"
            Id        = 7000
            StartTime = (Get-Date).AddMinutes(-10)
        } -MaxEvents 5 -ErrorAction SilentlyContinue |
            Where-Object { $_.Message -match [regex]::Escape($ServiceName) -or $_.Message -match "VPN Route WFP callout" } |
            Select-Object -First 1
        if ($evt) {
            $lines.Add("scm7000=$($evt.Message -replace '\s+', ' ')")
        }
    }
    catch {
    }

    return ($lines -join "; ")
}

function Set-SvrKernelDriverBinPath {
    param(
        [Parameter(Mandatory = $true)][string]$SysPath
    )

    $full = [System.IO.Path]::GetFullPath($SysPath)
    if (-not (Test-Path -LiteralPath $full)) {
        throw "Driver sys not found: $full"
    }

    $driver = Get-Service -Name $script:SvrDriverServiceName -ErrorAction SilentlyContinue
    if ($driver -and $driver.Status -eq "Running") {
        Stop-SvrWindowsServiceForPublish -ServiceName $script:SvrDriverServiceName
    }

    Write-SvrUpdateLogLine "runtime-repair: recreating kernel service binPath= `"$full`""
    Invoke-SvrSc -ArgumentString "stop $($script:SvrDriverServiceName)" -IgnoreErrors | Out-Null
    Start-Sleep -Milliseconds 400
    Invoke-SvrSc -ArgumentString "delete $($script:SvrDriverServiceName)" -IgnoreErrors | Out-Null
    Invoke-SvrSc -ArgumentString "create $($script:SvrDriverServiceName) type= kernel start= demand binPath= `"$full`" DisplayName= `"VPN Route WFP callout`"" | Out-Null
}

function Stop-SvrWindowsServiceForPublish {
    param(
        [Parameter(Mandatory = $true)][string]$ServiceName,
        [int]$TimeoutSeconds = 45
    )

    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if (-not $svc) {
        Write-SvrPipelineInfo -Name "runtime-stop" -Message "$ServiceName not installed"
        return
    }

    if ($svc.Status -eq "Stopped") {
        Write-SvrPipelineInfo -Name "runtime-stop" -Message "$ServiceName already Stopped"
        return
    }

    Write-SvrPipelineInfo -Name "runtime-stop" -Message "stopping $ServiceName"
    try {
        Stop-Service -Name $ServiceName -Force -ErrorAction Stop
    }
    catch {
        & sc.exe stop $ServiceName | Out-Null
    }

    try {
        $svc.WaitForStatus("Stopped", (New-TimeSpan -Seconds $TimeoutSeconds))
    }
    catch {
        throw "Service '$ServiceName' did not reach Stopped within ${TimeoutSeconds}s."
    }

    if ($ServiceName -eq $script:SvrDriverServiceName) {
        Start-Sleep -Seconds 2
    }
}

function Start-SvrWindowsServiceForPublish {
    param(
        [Parameter(Mandatory = $true)][string]$ServiceName,
        [int]$TimeoutSeconds = 45
    )

    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if (-not $svc) {
        throw "Cannot start '$ServiceName': service is not installed."
    }

    if ($svc.Status -eq "Running") {
        return
    }

    Write-SvrPipelineInfo -Name "runtime-start" -Message "starting $ServiceName"
    try {
        Start-Service -Name $ServiceName -ErrorAction Stop
    }
    catch {
        $svc.Refresh()
        if ($svc.Status -eq "Running") {
            return
        }

        if ($_.Exception.InnerException -and $_.Exception.InnerException.Message -match '\b1056\b') {
            $svc.Refresh()
            if ($svc.Status -eq "Running") {
                return
            }
        }

        $start = Invoke-SvrSc -ArgumentString "start $ServiceName" -IgnoreErrors
        $svc.Refresh()
        if ($svc.Status -eq "Running" -or $start.ExitCode -eq 0 -or $start.ExitCode -eq 1056) {
            # SCM state is authoritative; sc.exe exit/localization is diagnostic only.
        }
        elseif ($start.ExitCode -ne 0) {
            $diag = Get-SvrWindowsServiceStateDiagnostics -ServiceName $ServiceName -IncludeStartAttempt
            throw "Service '$ServiceName' start failed (sc exit $($start.ExitCode)). Diagnostics: $diag"
        }
    }

    try {
        $svc.WaitForStatus("Running", (New-TimeSpan -Seconds $TimeoutSeconds))
    }
    catch {
        $diag = Get-SvrWindowsServiceStateDiagnostics -ServiceName $ServiceName
        throw "Service '$ServiceName' did not reach Running within ${TimeoutSeconds}s. Diagnostics: $diag"
    }
}

function Stop-SvrCalloutDriverIfLoadedFromDirectory {
    param([Parameter(Mandatory = $true)][string]$Directory)

    if (-not (Test-SvrShouldStopDriverForDirectoryCleanup -Directory $Directory)) {
        return
    }

    Stop-SvrWindowsServiceForPublish -ServiceName $script:SvrDriverServiceName
}

function Stop-SvrPublishRuntimeForTreeSwap {
    param([Parameter(Mandatory = $true)][string]$PublishDir)

    Stop-AllSvrGuiProcesses -TimeoutSeconds 10 | Out-Null

    $product = Get-Service -Name $script:SvrProductServiceName -ErrorAction SilentlyContinue
    if ($product -and $product.Status -eq "Running") {
        Stop-SvrServiceForPublish -ServiceName $script:SvrProductServiceName -TimeoutSeconds 15 | Out-Null
    }

    Stop-SvrCalloutDriverIfLoadedFromDirectory -Directory $PublishDir
    Stop-SvrPublishOrphanProcesses -PublishDir $PublishDir -TimeoutSeconds 10
}

function Stop-SvrCalloutDriverIfBlockingDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$Directory,
        [string]$PublishParentDir
    )

    if (-not (Test-SvrShouldStopDriverForDirectoryCleanup -Directory $Directory -PublishParentDir $PublishParentDir)) {
        return
    }

    Write-SvrUpdateLogLine "publish-cleanup: stopping callout driver locking $Directory"
    Stop-SvrWindowsServiceForPublish -ServiceName $script:SvrDriverServiceName
}

function Restore-SvrPublishDriverImagePathFromSnapshot {
    param(
        [Parameter(Mandatory = $true)]$Snapshot
    )

    if (-not $Snapshot.DriverInstalled -or [string]::IsNullOrWhiteSpace($Snapshot.DriverImagePath)) {
        return
    }

    $driver = Get-Service -Name $script:SvrDriverServiceName -ErrorAction SilentlyContinue
    if (-not $driver) {
        return
    }

    $currentDriver = ConvertFrom-SvrKernelImagePath -ImagePath (
        Get-SvrServiceImagePathFromRegistry -ServiceName $script:SvrDriverServiceName
    )
    $expectedDriver = ConvertFrom-SvrKernelImagePath -ImagePath $Snapshot.DriverImagePath
    if (-not $expectedDriver -or ($currentDriver -and $currentDriver.Equals($expectedDriver, [StringComparison]::OrdinalIgnoreCase))) {
        return
    }

    Write-SvrUpdateLogLine "runtime-restore: reverting callout ImagePath to snapshot path=$expectedDriver"
    Set-SvrKernelDriverBinPath -SysPath $expectedDriver
}

function Repair-SvrPublishRuntimeImagePaths {
    param(
        [Parameter(Mandatory = $true)][string]$PublishDir,
        $Snapshot = $null
    )

    $serviceExe = Join-Path $PublishDir "SelectiveVpnRouter.Service.exe"
    $driverSys = Join-Path $PublishDir "driver\SelectiveVpnCallout.sys"

    $product = Get-Service -Name $script:SvrProductServiceName -ErrorAction SilentlyContinue
    if ($product) {
        $current = ConvertFrom-SvrKernelImagePath -ImagePath (
            (Get-CimInstance Win32_Service -Filter "Name='$($script:SvrProductServiceName)'").PathName
        )
        $expected = ConvertFrom-SvrKernelImagePath -ImagePath $serviceExe
        if ($current -and $expected -and -not ($current.Equals($expected, [StringComparison]::OrdinalIgnoreCase))) {
            if ($product.Status -eq "Running") {
                Stop-SvrWindowsServiceForPublish -ServiceName $script:SvrProductServiceName
            }

            $quoted = '"' + $expected + '"'
            Write-SvrUpdateLogLine "runtime-repair: sc config $($script:SvrProductServiceName) binPath= $quoted"
            & sc.exe config $script:SvrProductServiceName binPath= $quoted start= auto | Out-Null
            if ($LASTEXITCODE -ne 0) {
                throw "sc config $($script:SvrProductServiceName) failed with exit code $LASTEXITCODE"
            }
        }
    }

    $repairDriverToPublish = $true
    if ($Snapshot) {
        $repairDriverToPublish = Test-SvrShouldRepairDriverImagePathToPublishLayout -Snapshot $Snapshot -PublishDir $PublishDir
    }

    if (-not $repairDriverToPublish) {
        Write-SvrUpdateLogLine "runtime-repair: skipping callout ImagePath rewrite (driver registered outside publish tree)"
        return
    }

    if (-not (Test-Path -LiteralPath $driverSys)) {
        $stagingSys = Get-SvrDriverStagingSysPath -Root (Get-SvrRepoRoot) -Configuration Release
        if (Test-Path -LiteralPath $stagingSys) {
            $driverDir = Split-Path $driverSys -Parent
            New-Item -ItemType Directory -Force -Path $driverDir | Out-Null
            Copy-Item -LiteralPath $stagingSys -Destination $driverSys -Force
            Write-SvrUpdateLogLine "runtime-repair: copied staged driver -> publish runtime path"
        }
    }

    $driver = Get-Service -Name $script:SvrDriverServiceName -ErrorAction SilentlyContinue
    if ($driver -and (Test-Path -LiteralPath $driverSys)) {
        $currentDriver = ConvertFrom-SvrKernelImagePath -ImagePath (
            Get-SvrServiceImagePathFromRegistry -ServiceName $script:SvrDriverServiceName
        )
        $expectedDriver = ConvertFrom-SvrKernelImagePath -ImagePath $driverSys
        if ($currentDriver -and $expectedDriver -and -not ($currentDriver.Equals($expectedDriver, [StringComparison]::OrdinalIgnoreCase))) {
            Set-SvrKernelDriverBinPath -SysPath $expectedDriver
        }
    }
}

function Restore-SvrPublishRuntimeFromSnapshot {
    param(
        $Snapshot,
        [Parameter(Mandatory = $true)][string]$PublishDir
    )

    $startDriver = $Snapshot.DriverInstalled -and $Snapshot.DriverWasRunning
    $startProduct = $Snapshot.ProductInstalled -and $Snapshot.ProductWasRunning

    if ($startDriver) {
        Start-SvrWindowsServiceForPublish -ServiceName $script:SvrDriverServiceName
    }

    if ($startProduct) {
        $serviceExe = Join-Path $PublishDir "SelectiveVpnRouter.Service.exe"
        Start-SvrPublishedService -ServiceName $script:SvrProductServiceName -ServiceExe $serviceExe
    }
}
