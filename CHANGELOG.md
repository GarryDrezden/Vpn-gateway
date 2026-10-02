# Changelog

## Milestone — Connection lifecycle stability (`vpn-lifecycle-stability-pass-v1`)

- OpenVPN `IsConnected`: reject `Initialization Sequence Completed With Errors`; require successful completion line semantics.
- `ServiceSnapshot.VpnRoutingReady` for full pipeline readiness (UI + offline runners).
- Diagnostics: `vpn-resource-health`, `vpn-lifecycle-cleanup-check`, `vpn-lifecycle-reconnect-stress` (post-reconnect routing smoke), `vpn-connection-routing-smoke` (delegates to `transparent-routing`).
- Offline: `test-vpn-lifecycle-preflight.ps1`, `run-vpn-lifecycle-regression.ps1`; IPC service PID watch baseline fixes.
- Docs: [docs/vpn-lifecycle-stability.md](docs/vpn-lifecycle-stability.md).

## Milestone — Unicode ALE_APP_ID routing (`unicode-appid-routing-pass-v1`)

- **Root cause:** runtime ALE_APP_ID is byte-exact UTF-16LE; FwpmGetAppIdFromFileName0 can return filesystem casing that differs (e.g. Cyrillic Т vs т).
- **Production fix:** WfpAleAppIdBuilder — Fwpm canonical string → ToLowerInvariant() → owned UTF-16LE+NUL blob for FWPM_CONDITION_ALE_APP_ID (WfpSession.InstallAppFilter).
- **Removed** dual long/short APP_ID filter strategy from default identity paths (8.3 workaround not used in production).
- **Diagnostics:** wfp-runtime-appid-normalization-matrix, wfp-telegram-appid-acceptance; updated wfp-runtime-appid-case interpretation; blob diagnostic case E uses production builder.
- **Offline runner:** scripts/run-wfp-appid-regression.ps1 (matrix, blob, case, unicode probe, Telegram acceptance).
- **Tests:** WfpAleAppIdBuilderTests (Cyrillic/ASCII bytes, dispose, file pipeline).

## 0.2.0 — VPN Route MVP foundation

- Kept `SelectiveVpn.V0` transport PoC.
- Core: application/domain/CIDR rules, DIRECT precedence, no parent inheritance, route ownership, DNS cache, config + crash state, OpenVPN parsers.
- Network: adapters, owned IP Helper routes, OpenVPN `--route-nopull` + optional `--disable-dco`, `IP_UNICAST_IF`, dynamic WFP session, callout IOCTL client.
- Proxy: local TCP relay (WFP redirect context + SOCKS5), VPN-bound outbound sockets.
- Service: named-pipe IPC, diagnostics/Test Center backend, fail-open cleanup, emergency restore.
- WPF GUI: rules, live flows, wizard, tray, emergency restore.
- Probe: `--show-network --tcp --http --dns --watch --via-proxy --bind-if`.
- Minimal KMDF connect-redirect callout: fail-open on last device close, proxy-PID/loop guards, GET_STATUS IOCTL, WDK-detecting build/install/check scripts, Test Center WFP DRIVER block, Probe `--spawn`/`--tcp6`. Driver binary is not claimed loaded until WDK build + install succeed.
- Docs: architecture, research log, manual tests, driver setup, security, limitations.

## 0.1.x — V0 / V0.1 research

- V0: Direct + OpenVPN coexistence without default-route steal; `IP_UNICAST_IF` applied; Internet via selected NIC **not** reached (WSA 10051).
- V0.1: destination `/32` via OpenVPN `--route` (not per-process).
