# Portable distribution and bootstrap (V1)

## What “portable” means

VPN Route ships as a **ZIP folder**, not an MSI/Inno installer.

- Extract anywhere on a local drive (not UNC in V1).
- Run `SelectiveVpnRouter.App.exe` without installing the app into Program Files.
- **One-time UAC** registers product-owned **Windows Service** and **callout driver**.
- User settings stay in `%ProgramData%\SelectiveVpnRouter` (and UI prefs in `%LocalAppData%\SelectiveVpnRouter`).

This is **portable distribution + bootstrap**, not zero-footprint portable.

## Package layout

```
VPN-Route-<version>-x64/
  SelectiveVpnRouter.App.exe
  SelectiveVpnRouter.Service.exe
  SelectiveVpnRouter.Bootstrap.exe
  SelectiveVpnRouter.Probe.exe
  (+ self-contained .NET runtime and dependencies)
  driver/
    SelectiveVpnCallout.sys
    SelectiveVpnCallout.inf
    SelectiveVpnCallout.cat   (optional, when signed)
  portable-manifest.json
  README.txt
```

Build: `scripts/build-portable.ps1`  
Artifact: `artifacts/portable/VPN-Route-<version>-x64.zip`

Developer fast path remains `scripts/update-desktop.ps1` (framework-dependent publish under `artifacts/publish`).

## First run

1. Extract ZIP.
2. Launch App (self-contained, no separate .NET install).
3. If bootstrap is not ready, the app shows **Подготовить** / **Обновить**.
4. UAC → `SelectiveVpnRouter.Bootstrap.exe repair`.
5. Normal Home UI; later launches **without UAC**.

## OpenVPN prerequisite

OpenVPN Community (and TAP/DCO as required by your profile) is **not** bundled. Configure `openvpn.exe` in Settings after bootstrap.

Corporate Work VPN stays **external** (OpenVPN GUI). In-app Work VPN remains hidden unless `VPN_ROUTE_ENABLE_WORK_VPN=1`.

## Updates and relocation

- **Update:** extract new ZIP to a new folder → launch App → **Обновить** → UAC rebinds service to the new folder. Config in ProgramData is preserved.
- **Move folder:** same as update (service `ImagePath` must match current package).
- **Same-folder overwrite** while service is running is not supported in V1.

## Remove system components

Elevated:

```text
SelectiveVpnRouter.Bootstrap.exe remove --root "<portable folder>"
```

Removes service and callout driver registration; **does not** delete portable files or user config.

## Driver signing

Bootstrap **does not** enable test signing or install trust anchors. If `SelectiveVpnCallout.sys` is not Authenticode-trusted and test signing is off, repair fails with an explicit driver signing message.

Portable packaging can be ready while **production driver distribution** remains blocked until proper signing.

## CLI

```text
SelectiveVpnRouter.Bootstrap.exe status [--root <dir>]
SelectiveVpnRouter.Bootstrap.exe repair [--root <dir>]   # elevated
SelectiveVpnRouter.Bootstrap.exe remove [--root <dir>]    # elevated
```

`status` prints JSON (`PortableBootstrapStatus`).

## Preflight

```powershell
powershell -ExecutionPolicy Bypass -File scripts/test-portable-preflight.ps1
```
