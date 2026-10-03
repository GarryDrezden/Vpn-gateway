# VPN Route

Windows 10/11 x64 app that keeps **normal Direct internet** as the system default, can coexist with a **corporate VPN**, and sends **only selected applications** (and optionally selected destinations) through a **personal OpenVPN** tunnel.

Quick scenario: **route Cursor through personal VPN while Git stays Direct.**

## Milestones (verified on Windows 10/11 x64)

### Milestone 1 — `per-process-routing-pass-v1`

Controlled **Probe.exe** path: transparent per-process TCP routing with simultaneous Direct isolation.

Application rule → WFP `FWPM_CONDITION_ALE_APP_ID` → `FWPM_LAYER_ALE_CONNECT_REDIRECT_V4` → KMDF callout (`SelectiveVpnCallout.sys`) → localhost transparent proxy → redirect context recovery → VPN-bound outbound socket.

### Milestone 2 — `real-app-vpn-egress-pass-v1`

Real external application verified: `C:\Windows\System32\curl.exe` with temporary APP_ID rule.

- HTTPS `200 OK` via `curl --resolve` (DNS bypassed for controlled test)
- VPN public IP observed (`91.184.250.53`)
- Same executable without rule returns Direct public IP (`83.143.157.1`)
- Proxy flow: `vpnOutboundConnected=True`, `status=Closed`, `egressVerified=True`

Full chain: curl.exe → WFP filter → callout → transparent proxy → VPN-bound outbound → TLS/HTTP → VPN egress.

### Milestone 3 — `browser-vpn-egress-pass-v1`

Real browser verified: `C:\Program Files (x86)\Google\Chrome\Application\chrome.exe` with temporary APP_ID rule.

- Chrome opened `https://api.ipify.org` and showed VPN public IP (`91.184.250.53`)
- Direct traffic without rule stays Direct (`curl.exe -4 https://api.ipify.org` → `83.143.157.1`)
- Test Center target `api.ipify.org:443` → **TARGET CLOSED / PASS** (`WFP=True`, `Proxy=True`, `Context=True`, `Bound=True`, `Connected=True`, `Route=Vpn`)
- Target flow matching isolates resolved target IPs; background Chrome flows (Google/CDN) do not overwrite target PASS
- VPN adapter readiness: APIPA/Tentative rejected; managed DAD discovery; safe ConnectVpn IPC timeout + rollback

Full chain: chrome.exe → ALE APP_ID / WFP → callout → transparent proxy → VPN-bound outbound → HTTPS → VPN egress.

### Milestone 4 — `unicode-appid-routing-pass-v1`

Unicode and non-ASCII executable paths: **PASS** (normalized ALE_APP_ID after Fwpm canonical string).

- Normalization matrix: `counterExamples=0` (ASCII, Cyrillic, Latin umlaut, mixed paths)
- Unicode Probe redirect end-to-end: Outcome PASS, VPN public IP (`91.184.250.53`)
- Telegram (`Telegram Desktop\Telegram.exe` under a Unicode user profile): production filter + UI flows `Route=VPN`

Technical note: [docs/unicode-ale-app-id.md](docs/unicode-ale-app-id.md)

Offline regression (after VPN Connected):

```powershell
.\scripts\test-runtime-diagnostic-preflight.ps1
.\scripts\run-wfp-appid-regression.ps1
```

### Milestone 5 — `vpn-lifecycle-stability-pass-v1`

Connect/disconnect/reconnect cleanup, routing after second reconnect, clean disconnected health.

- `VpnRoutingReady` gates UI and offline runners (full pipeline, not log line alone).
- OpenVPN parser rejects `Initialization Sequence Completed With Errors`.
- Offline: `test-vpn-lifecycle-preflight.ps1`, `run-vpn-lifecycle-regression.ps1` (initial + post-reconnect routing smoke, final cleanup).

Technical note: [docs/vpn-lifecycle-stability.md](docs/vpn-lifecycle-stability.md)

### Verified now (TCP IPv4)

- Per-process connect-redirect for selected `.exe` processes (ASCII and Unicode paths)
- Probe isolation + real-app curl egress + real Chrome browser egress + Telegram (Unicode path)
- WFP ALE APP_ID, callout redirect, proxy, redirect context, VPN-bound outbound
- Real-app flow history with target matching (background flows do not false-pass target)

### Not verified yet (do not assume support)

- In-app **Work/corporate VPN** connect (experimental code only; hidden unless `VPN_ROUTE_ENABLE_WORK_VPN=1`). Use **OpenVPN GUI** for corporate VPN; VPN Route handles selective app VPN only.
- Full UDP / QUIC per-process routing
- IPv6 VPN egress end-to-end
- Arbitrary DNS routing for all apps

### In progress — portable distribution / bootstrap V1

End-user delivery is a **self-contained win-x64 ZIP** (no separate .NET 10 Runtime install). Windows **service + WFP driver** are registered once via elevated `SelectiveVpnRouter.Bootstrap.exe`; the App runs unelevated after bootstrap is **Ready**.

Full guide: [docs/portable-distribution.md](docs/portable-distribution.md)

**Build portable package** (from repo root):

```powershell
.\scripts\build-portable.ps1
.\scripts\test-portable-preflight.ps1
```

Output: `artifacts\portable\VPN-Route-<version>-x64.zip` and matching folder.

**First run (extract anywhere, e.g. `C:\Tools\VPN Route\`)**

1. Run `SelectiveVpnRouter.App.exe` — no UAC if service/driver already match this folder.
2. If the overlay appears, choose **Подготовить** / **Обновить** → UAC → `SelectiveVpnRouter.Bootstrap.exe repair`.
3. After **Ready**, configure **OpenVPN Community** path in Settings if needed (not bundled).

**Update:** extract a newer ZIP over the same folder (or a new folder) and run **Обновить** when `VersionMismatch` / relocation is detected.

**Move folder:** copy the whole directory; launch App → **NeedsRepair** → **Обновить** (rebinds service `ImagePath`).

**Remove system components (keep config):** elevated `SelectiveVpnRouter.Bootstrap.exe remove --root "<portable dir>"` — preserves `%ProgramData%\SelectiveVpnRouter\config.json` and `%LocalAppData%\SelectiveVpnRouter\ui.json`.

Developer deploy (framework-dependent publish) remains `scripts\update-desktop.ps1` → `artifacts\publish`.


## Developer update command

Primary dev workflow from repo root (elevated PowerShell):

```powershell
.\scripts\update-desktop.ps1
```

Runs **build -> tests -> publish -> service restart -> runtime smoke** with a short console summary. Full output is saved automatically to `artifacts/logs/update-desktop-*.log`.

Verbose console output:

```powershell
.\scripts\update-desktop.ps1 -VerboseOutput
```
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

3. For normal desktop use, see **[Desktop launch](docs/DESKTOP_LAUNCH.md)** — publish once, install the Windows Service once, then double-click `SelectiveVpnRouter.App.exe`.

   Dev console mode (elevated): `SelectiveVpnRouter.Service.exe --console`

   Or install service: `powershell -ExecutionPolicy Bypass -File scripts\publish-desktop.ps1` then `scripts\install-service.ps1`

4. Start `SelectiveVpnRouter.App.exe` (does not need elevation).
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

- [Desktop launch](docs/DESKTOP_LAUNCH.md)
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
