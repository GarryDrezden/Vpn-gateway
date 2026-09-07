# Changelog

## 0.2.0 — Selective VPN Router MVP foundation

- Kept `SelectiveVpn.V0` transport PoC.
- Core: application/domain/CIDR rules, DIRECT precedence, no parent inheritance, route ownership, DNS cache, config + crash state, OpenVPN parsers.
- Network: adapters, owned IP Helper routes, OpenVPN `--route-nopull` + optional `--disable-dco`, `IP_UNICAST_IF`, dynamic WFP session, callout IOCTL client.
- Proxy: local TCP relay (WFP redirect context + SOCKS5), VPN-bound outbound sockets.
- Service: named-pipe IPC, diagnostics/Test Center backend, fail-open cleanup, emergency restore.
- WPF GUI: rules, live flows, wizard, tray, emergency restore.
- Probe: `--show-network --tcp --http --dns --watch --via-proxy --bind-if`.
- Minimal KMDF connect-redirect callout source + INF (build with WDK).
- Docs: architecture, research log, manual tests, driver setup, security, limitations.

## 0.1.x — V0 / V0.1 research

- V0: Direct + OpenVPN coexistence without default-route steal; `IP_UNICAST_IF` applied; Internet via selected NIC **not** reached (WSA 10051).
- V0.1: destination `/32` via OpenVPN `--route` (not per-process).
