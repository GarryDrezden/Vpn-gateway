# Driver setup

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
