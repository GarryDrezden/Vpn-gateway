# Driver setup

Transparent per-process TCP (`Cursor.exe` / `Probe.exe` → local proxy) needs `SelectiveVpnCallout.sys`. The C# solution **builds without the driver**. **Source on disk is not a loaded driver.** Test Center will FAIL `wdk-build` / `driver-binary` until WDK produces a SYS and you load it.

Target: Windows 10/11 **x64**.

Signing / Secure Boot / HVCI: [DRIVER_SIGNING.md](DRIVER_SIGNING.md). App and scripts never flip those settings.

## Prerequisites (exact)

Already observed on a typical VS 2022 + user-mode SDK machine: **MSVC and `Windows Kits\10\Include\<ver>\um` are not enough.** You also need kernel WDK bits:

1. Visual Studio 2022 — workload **Desktop development with C++**
2. [Windows Driver Kit](https://learn.microsoft.com/en-us/windows-hardware/drivers/download-the-wdk) matching the installed Windows SDK (e.g. 10.0.22621)
3. WDK Visual Studio integration so this folder exists:

   `MSBuild\Microsoft\VC\v170\Platforms\x64\PlatformToolsets\WindowsKernelModeDriver10.0`

4. Headers:

   - `Windows Kits\10\Include\<ver>\km\fwpsk.h`
   - `Windows Kits\10\Include\wdf\kmdf\<ver>\wdf.h`

## Build

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-driver.ps1
powershell -ExecutionPolicy Bypass -File scripts\build-driver.ps1 -Configuration Debug
```

PASS prints the path `artifacts\driver\Release\SelectiveVpnCallout.sys`.  
FAIL names the missing WDK component and does **not** pretend the driver is implemented.

## Check (read-only)

```powershell
powershell -ExecutionPolicy Bypass -File scripts\check-driver.ps1
```

Prints PASS/FAIL/WARNING for elevation, WDK, binary, signature, Secure Boot, TESTSIGNING, HVCI, service, `\\.\SelectiveVpnCallout`.

## Install / start / stop / uninstall

Elevated:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\install-driver.ps1
powershell -ExecutionPolicy Bypass -File scripts\uninstall-driver.ps1
```

Install **refuses** if the SYS is untrusted and TESTSIGNING is off. It will not enable TESTSIGNING for you.

## Fail-open lifecycle

1. Service starts → opens `\\.\SelectiveVpnCallout` and keeps the handle.
2. `IOCTL_SET_TARGET` arms redirect (proxy PID + port).
3. User-mode WFP session uses `FWPM_SESSION_FLAG_DYNAMIC` and adds APP_ID filters that point at the callout.
4. Service stop/crash → dynamic filters/provider objects disappear; the device handle closes; the callout **disables redirect** (`gEnabled=FALSE`) even if `.sys` stays loaded. Classify then PERMITs (Direct).
5. If the proxy PID is already dead, classify fail-opens without waiting for handle close.

Owned high-metric `0.0.0.0/0` is user-mode IP Helper state (crash-state JSON). It does not capture preferred Direct default (metric 9000). Startup and Emergency Restore delete owned routes.

## Device ACL

`SDDL_DEVOBJ_SYS_ALL_ADM_ALL` (SYSTEM + Administrators).


Transparent per-process TCP (`Cursor.exe` → local proxy) needs the KMDF callout `SelectiveVpnCallout.sys`. The C# solution **builds without the driver**. Test signing is **not** turned on by the app.

Target: Windows 10/11 **x64**.

## Prerequisites

- Visual Studio 2022 with **Desktop development with C++**
- [Windows Driver Kit (WDK)](https://learn.microsoft.com/en-us/windows-hardware/drivers/download-the-wdk) matching the SDK
- Windows SDK 10.0.22621 or newer (headers under `Windows Kits\10\Include`)

## Build

Open `driver\SelectiveVpnCallout\SelectiveVpnCallout.vcxproj` in Visual Studio (WDK toolset `WindowsKernelModeDriver10.0`).

Build **Release | x64**. Output: `SelectiveVpnCallout.sys` + INF.

The driver:

- Registers a WFP callout at ALE CONNECT_REDIRECT_V4
- Redirects matching connects to `127.0.0.1:proxyPort`
- Exposes `\\.\SelectiveVpnCallout` for IOCTL (proxy PID + port)
- Does **not** parse HTTP, TLS, or DNS

## Test signing (development only)

The app never enables test signing. You must do this yourself on a **dev** machine:

```powershell
bcdedit /set testsigning on
# reboot
```

Disable later:

```powershell
bcdedit /set testsigning off
```

Production requires a proper Authenticode/EV attestation signature. This repo does not ship a catalog signature.

## Install

From an elevated PowerShell, after building (adjust `BinPath`):

```powershell
powershell -ExecutionPolicy Bypass -File scripts\install-driver.ps1 -SysPath "C:\path\to\SelectiveVpnCallout.sys"
```

Or manually:

```powershell
sc.exe create SelectiveVpnCallout type= kernel start= demand binPath= "C:\path\to\SelectiveVpnCallout.sys"
sc.exe start SelectiveVpnCallout
```

PnP INF install (`pnputil /add-driver ...`) is preferred when the INF copies the .sys into `%SystemRoot%\System32\drivers`.

Confirm: Test Center **Test application routing** should say the callout is loaded after Connect.

## Uninstall / cleanup

```powershell
powershell -ExecutionPolicy Bypass -File scripts\uninstall-driver.ps1
```

```powershell
sc.exe stop SelectiveVpnCallout
sc.exe delete SelectiveVpnCallout
del "$env:SystemRoot\System32\drivers\SelectiveVpnCallout.sys" -ErrorAction SilentlyContinue
```

WFP filters added by the service use a **dynamic** session and disappear when the service dies. The callout **registration** lives as long as the driver is loaded — uninstall the driver after stopping the service.

## Device ACL

The control device is created with `SDDL_DEVOBJ_SYS_ALL_ADM_ALL` (SYSTEM + Administrators). Do not loosen this to Everyone.
