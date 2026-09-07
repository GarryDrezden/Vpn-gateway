# Known limitations

This is an MVP foundation, not a finished consumer VPN client.

## Transparent per-process TCP needs a **built and loaded** callout

User-mode cannot classify `FWPM_LAYER_ALE_CONNECT_REDIRECT_V4`. `driver/` is source. This machine may have VS + user-mode SDK **without** WDK `km` headers. Until `scripts\build-driver.ps1` PASSes and `scripts\check-driver.ps1` opens `\\.\SelectiveVpnCallout`, Application rules do not transparently hijack `Probe.exe` / `Cursor.exe`. Test Center **Test transparent routing** is the only test allowed to print `PER-PROCESS ISOLATION: PASS`. `Probe --via-proxy` still tests proxy + `IP_UNICAST_IF`.

## UDP / QUIC

No per-process UDP routing. QUIC (UDP/443) can bypass the TCP proxy. The “Block QUIC” checkbox is persisted for a future WFP UDP/443 block; it does **not** currently install that filter. It will never block all UDP without an explicit, warned control.

## Domain wildcards

`*.example.com` destination routes are built from DNS of the **apex** `example.com` (plus exact names you add). There is no DNS packet interceptor, so new subdomains are not auto-learned. Domain/IP mode is **global for destination**.

## High-metric VPN default

Required for proxy reachability after V0’s 10051 result. If some other software also binds to the tunnel NIC, it could use that route. Unbound user apps should still prefer Direct.

## IPv6

Default blocks IPv6 connects for VPN-routed APP_IDs. Full IPv6-via-VPN (policy VpnIfAvailable) is only meaningful when the tunnel has a global IPv6 address; CONNECT_REDIRECT_V6 callout is not implemented yet.

## Credentials / OpenVPN UI

Service has no window. Interactive `auth-user-pass` without a file will fail. Passwords are not stored in JSON.

## WFP P/Invoke layout

Filter add uses hand-marshaled `FWPM_FILTER0`. If a given Windows build rejects filters, Test Center / service.log will show the error; Direct internet still works (fail open).

## Driver build

The `.sys` is **source-only** in this repo until built with WDK. C# `dotnet build` does not compile the driver.

## Parent process

No “include children” mode. That is intentional so Cursor cannot pull `git.exe` into the personal VPN.

## Installer

No MSI yet. Use `dotnet publish` + `scripts\install-service.ps1` / uninstall scripts.

## V0.1 destination /32 experiment

Not the Application-rule path. Do not resurrect it as process routing.
