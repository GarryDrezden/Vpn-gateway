# Known limitations

This is an MVP foundation, not a finished consumer VPN client.

## Transparent per-process TCP (verified on Windows x64)

End-to-end TCP path is verified: Application rule → WFP ALE APP_ID → connect-redirect callout → localhost proxy → redirect context → VPN-bound outbound. Test Center **Test transparent routing** is the automated Probe acceptance test. **Проверка реального приложения** adds a temporary APP_ID rule for any user-selected `.exe` and observes WFP/proxy flows. Milestone `browser-vpn-egress-pass-v1` verified real Chrome HTTPS egress to VPN public IP while Direct traffic without the rule stays Direct; target flow matching (`api.ipify.org:443`) PASS even when background Chrome flows exist.

You still need a **built and loaded** callout (`scripts\build-driver.ps1`, `scripts\check-driver.ps1` → `\\.\SelectiveVpnCallout`). Without the driver, Application rules cannot transparently hijack TCP connects.

## Application DNS / libcurl resolver

Application WFP rules target **TCP connect** layers only (`ALE_CONNECT_REDIRECT_V4`, optional `ALE_AUTH_CONNECT_V6` block). They do **not** install UDP or DNS WFP filters.

Some clients (notably Windows `curl.exe`) may fail name resolution with `getaddrinfo() thread failed to start` while a temp VPN rule is active, even though `Resolve-DnsName` works system-wide. This is libcurl's threaded resolver failing to start a worker thread — not proof that TCP routing is broken. Verify TCP/VPN egress with `curl --resolve` (preserves SNI/hostname, skips DNS lookup only).

The kernel callout currently bypasses redirect only for `127.0.0.1:<proxyPort>` and proxy PID. Full `127.0.0.0/8` bypass in the driver is recommended if local loopback TCP must never be redirected; managed proxy also bypasses loopback destinations that reach the proxy.

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
