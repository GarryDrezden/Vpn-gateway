# Selective VPN Router

Windows 10/11 x64 app that keeps **normal Direct internet** as the system default, can coexist with a **corporate VPN**, and sends **only selected applications** (and optionally selected destinations) through a **personal OpenVPN** tunnel.

Quick scenario: **route Cursor through personal VPN while Git stays Direct.**

## What this is

| Piece | Role |
| --- | --- |
| `SelectiveVpnRouter.App` | Unelevated WPF GUI, tray, Test Center |
| `SelectiveVpnRouter.Service` | Elevated backend (Windows Service or `--console`) |
| `SelectiveVpnRouter.Probe` | Standalone route probe |
| `SelectiveVpn.V0` | Earlier transport PoC (kept; do not throw away) |
| `driver/SelectiveVpnCallout` | Minimal KMDF WFP connect-redirect callout (needed for *transparent* per-process TCP) |

Persistent config: `%ProgramData%\SelectiveVpnRouter\config.json`  
UI prefs: `%LocalAppData%\SelectiveVpnRouter\`

## Route Cursor through VPN while Git remains Direct

1. Install [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and [OpenVPN Community](https://openvpn.net/community-downloads/) (`openvpn.exe`, not OpenVPN 3).
2. Build:

```powershell
cd E:\Работа\OSPanel\domains\vpn-gateway
dotnet build SelectiveVpnRouter.sln -c Release
dotnet test SelectiveVpnRouter.sln -c Release
```

3. Start the **elevated** service (once per session, or install it):

```powershell
# from the Service output directory
.\SelectiveVpnRouter.Service.exe --console
```

Or: `powershell -ExecutionPolicy Bypass -File scripts\install-service.ps1`

4. Start `SelectiveVpnRouter.App.exe` (does not need to stay elevated).
5. Setup wizard: choose `openvpn.exe`, your `.ovpn`, optionally **Compatibility mode (disable DCO)** if DCO fights another VPN driver.
6. Connect. The personal tunnel is started with `--route-nopull` so it does **not** replace the system default route.
7. Add rules:
   - Application `Cursor.exe` → **VPN**
   - Application `git.exe` → **DIRECT** (filename match; does not inherit from Cursor)
8. Optional: add `SelectiveVpnRouter.Probe.exe` → VPN, then:

```powershell
.\SelectiveVpnRouter.Probe.exe --http https://api.ipify.org
```

A second copy of Probe **without** a VPN application rule should stay Direct.

**Transparent** `Cursor.exe` TCP → VPN requires the WFP callout driver (see [docs/DRIVER_SETUP.md](docs/DRIVER_SETUP.md)). Until the driver is loaded, Test Center reports that honestly; `Probe --via-proxy 127.0.0.1:<proxyPort>` still proves the VPN-bound proxy path.

## Honest routing modes

- **Application** — true per-process TCP (WFP ALE APP_ID). `git.exe` launched by Cursor is still `git.exe`.
- **Domain / IP / CIDR** — **global for that destination** (all processes). The UI says so. This is *not* per-process routing.

## Fail-open

If the GUI, service, OpenVPN, or driver policy goes away, Direct internet must keep working. Use **EMERGENCY RESTORE** to drop only *this app's* routes, WFP filters, and managed OpenVPN.

## Docs

- [Architecture](docs/ARCHITECTURE.md)
- [Network research](docs/NETWORK_ARCHITECTURE_RESEARCH.md)
- [Manual test plan](docs/MANUAL_TEST_PLAN.md)
- [Driver setup](docs/DRIVER_SETUP.md)
- [Driver signing](docs/DRIVER_SIGNING.md)
- [Troubleshooting](docs/TROUBLESHOOTING.md)
- [Security](docs/SECURITY.md)
- [Known limitations](docs/KNOWN_LIMITATIONS.md)
- [V0 transport PoC](docs/V0.md)
- [How to run V0](docs/HOW_TO_RUN.md)
