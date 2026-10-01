# Architecture

VPN Route splits **unelevated UI** from an **elevated backend**.

```
SelectiveVpnRouter.App (WPF, tray)
        |
        |  named pipe \\.\pipe\SelectiveVpnRouter
        |  (localhost, ACL: SYSTEM / Administrators / Authenticated Users, Network SID denied)
        v
SelectiveVpnRouter.Service  (LocalSystem or admin --console)
        |
        +-- OpenVPN (--route-nopull, optional --disable-dco, localhost management + random password file)
        +-- Owned IPv4 routes (high-metric 0.0.0.0/0 on the tunnel NIC + optional Domain/CIDR host routes)
        +-- TransparentTcpProxy (127.0.0.1, SOCKS5 + WFP-redirected accepts)
        +-- WFP user-mode session (FWPM_SESSION_FLAG_DYNAMIC)
        +-- Optional KMDF callout (ALE CONNECT_REDIRECT_V4)
```

## Projects

| Project | Responsibility |
| --- | --- |
| Core | Rules, matching, config JSON, route ownership plan, OpenVPN log parsing, IPC DTOs |
| Network | Adapters, IP Helper routes, `IP_UNICAST_IF`, OpenVPN process, WFP P/Invoke, driver IOCTL |
| Proxy | Local TCP relay; outbound bind to VPN interface |
| Service | Windows Service / console host, named pipe, diagnostics, crash reconcile |
| App | WPF GUI, wizard, Test Center, tray |
| Probe | `--show-network`, `--tcp`, `--http`, `--dns`, `--via-proxy`, `--watch` |
| V0 | Research PoC kept intact |

## Per-process TCP (application rules)

1. User-mode adds one WFP filter per VPN-routed `.exe` (`FWPM_CONDITION_ALE_APP_ID`) at `FWPM_LAYER_ALE_CONNECT_REDIRECT_V4` pointing at the callout.
2. The KMDF callout redirects the connect to `127.0.0.1:proxyPort` and stores the original IPv4 destination in redirect context.
3. The proxy queries `SIO_QUERY_WFP_CONNECTION_REDIRECT_*`, connects to the original destination, binds the outbound socket with `IP_UNICAST_IF` to the OpenVPN interface, and copies bytes.
4. `SIO_SET_WFP_CONNECTION_REDIRECT_RECORDS` on the outbound socket plus excluding the proxy PID prevents redirect loops.
5. Direct-rule executables get **no** redirect filter. Parent/child is **not** inherited.

Without the driver, application filters cannot classify at CONNECT_REDIRECT. The proxy still works for **explicit** SOCKS (`Probe --via-proxy`).

## Why a high-metric VPN default exists

V0 proved `IP_UNICAST_IF` alone yields `WSAENETUNREACH` (10051): the tunnel NIC has no route to arbitrary Internet destinations if OpenVPN did not install a default. The service therefore adds an **owned** `0.0.0.0/0` on the **VPN interface only**, metric **9000**. Windows continues to use the normal Direct default (lower metric) for unbound sockets. The proxy is bound to the VPN NIC. This is **not** `redirect-gateway` / `0.0.0.0/1`. It is removed on stop, emergency restore, and stale crash cleanup.

## Domain / CIDR

A reconcile loop turns VPN Domain/CIDR rules into owned host/prefix routes via the tunnel gateway. Direct Domain/CIDR rules win for that destination. This traffic is **not** process-isolated.

## IPv6

Default: **BlockForVpnRoutedApps** — user-mode WFP **block** at `ALE_AUTH_CONNECT_V6` + APP_ID so a v4-only tunnel does not leak via Direct IPv6. Configurable in the wizard.

## UDP / QUIC

MVP is **TCP-only** for per-process routing. UDP is unsupported. Optional “block QUIC UDP/443” is stored in config for a later filter; it is not silently blocking all UDP today.

## Fail-open

WFP management objects (filters, the session-scoped callout object, sublayer, provider) are added on a **dynamic** session (`FWPM_SESSION_FLAG_DYNAMIC`). When the service process dies, the BFE drops those objects. New connects are no longer redirected.

The KMDF driver may remain loaded. It must not keep hijacking TCP:

- Last close of `\\.\SelectiveVpnCallout` clears `gEnabled` (service crash closes the handle).
- Classify also fail-opens if the proxy PID is gone, if redirect is not armed, or if the connecting PID is the proxy (loop).
- Pause routing issues `IOCTL_SET_TARGET` with Enabled=0.

Lifecycle:

```
service start
  → open dynamic WFP session
  → open driver device (handle held)
  → add APP_ID filters + IPv6 block filters
  → IOCTL arm proxy pid/port
service stop / crash
  → session destroyed → filters gone
  → device handle closed → driver gEnabled=FALSE
  → OpenVPN job kill-on-close
  → owned routes removed on next start / Emergency Restore
```

Test Center **Kill service (fail-open)** (confirm) exercises the crash path. Expect Direct internet to keep working; restart the service after.

## Owned high-metric VPN default

`0.0.0.0/0` on the tunnel NIC, metric **9000**, reason `vpn-transport-high-metric`, recorded in crash-state. Test Center **Preferred default route** prints the system preferred default vs this owned fallback. Stop / Emergency Restore / startup cleanup remove it.

Emergency Restore touches only owned routes, owned WFP objects, and the managed OpenVPN child.
