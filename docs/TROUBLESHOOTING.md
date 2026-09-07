# Troubleshooting

## Service: not connected

Start `SelectiveVpnRouter.Service.exe --console` **as Administrator**, or install the Windows Service (`scripts\install-service.ps1`). The GUI talks over `\\.\pipe\SelectiveVpnRouter`.

## OpenVPN exits immediately / AUTH

Hidden service windows cannot answer a TTY password prompt. Use a profile that already authenticates (certificates or `auth-user-pass` file next to the `.ovpn`). Logs redact password-like lines.

## Direct internet died after Connect

1. Press **EMERGENCY RESTORE**.
2. If you connected vanilla OpenVPN *outside* this app (without `route-nopull`), it may have installed `redirect-gateway`. This app does not uninstall routes it does not own.
3. Enable **Compatibility mode (disable DCO)** if DCO previously broke the stack when another VPN was present.
4. Check for a **low-metric** default on the personal tunnel (`Probe --show-network`). Owned metric 9000 is expected; metric like 0–50 on the tunnel is not.

## Probe via VPN fails with WSAENETUNREACH

Tunnel is up but the proxy cannot route. Confirm owned high-metric default exists after Connect. Confirm `route-gateway` appeared in the OpenVPN log. TAP vs TUN guessing of `.1` can be wrong — check the log for `route-gateway` / `ifconfig`.

## Application rule does nothing

Callout driver not loaded. Test Center will WARNING. SOCKS `Probe --via-proxy` still works. See [DRIVER_SETUP.md](DRIVER_SETUP.md).

## git goes through VPN

You added a **Domain/IP** rule for github.com, or a `/32` that git uses. Domain/IP is global. Use an Application DIRECT rule for `git.exe` and do not add GitHub as a Domain VPN rule.

## Cursor works, Agent does not

Agent may be another executable (`Cursor.exe` vs helper). Live connections show the real path — add that EXE as a VPN application rule. Child `git.exe` will not follow Cursor.

## IPv6 sites bypass VPN

Set IPv6 policy to Block/Auto (default). Allow Direct is leaky by design.

## Stale routes after crash

Restart the service once (startup cleanup) or Emergency Restore. IP Helper routes persist across process death; crash-state JSON is how we remember what we added.
