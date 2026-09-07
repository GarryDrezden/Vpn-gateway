# Network architecture research

This document records what was tried in V0/V0.1 and what the MVP implements. It is a research log, not marketing.

## Goal

```
selected_process.exe  →  personal OpenVPN interface   (TCP)
everything else       →  normal Windows routing
```

Constraints: do not steal the system default route; corporate VPN must keep working; `git.exe` must not follow Cursor’s policy; fail open.

## V0 result (this machine / previous PoC)

OpenVPN with `--route-nopull` can bring a tunnel up **without** changing Direct default or system DNS.

Binding *this process’s* socket with `IP_UNICAST_IF` to the tunnel NIC applied successfully, then **failed** to reach the Internet: **WSA 10051 / WSAENETUNREACH**.

Conclusion: interface selection is necessary but **not sufficient**. The VPN NIC has no route to arbitrary destinations unless something installs a path via the tunnel gateway.

V0 explicitly refused to “fix” that with `0.0.0.0/0`, `0.0.0.0/1`, or `128.0.0.0/1` because the PoC was measuring coexistence, not cheating with a stolen default.

## Why the route table cannot do true per-process routing

A Windows IPv4 route is `(destination prefix → interface/next-hop)`. It has **no** application identity.

If you watch Cursor, learn `api2.cursor.sh` → `1.2.3.4`, and add `1.2.3.4/32` via the VPN, then **git, chrome, and every other process** to that IP also take the VPN. That is destination routing. It is allowed in this product **only** as Domain/IP mode, labeled as global.

## Options investigated

### A. Destination `/32` from process observation — rejected as “process routing”

Works for “send this IP via VPN” and is exactly V0.1’s experiment. Forbidden as the Application-rule implementation.

### B. WinDivert / WinSock LSP / undocumented hooks — rejected as primary path

WinDivert is packet-level, powerful, and easy to get wrong (captures more than one process, UDP, local traffic). LSP is deprecated and fragile. Not fail-open by default.

### C. User-mode-only WFP callout — impossible for CONNECT_REDIRECT classify

`FWPM_LAYER_ALE_CONNECT_REDIRECT_V4` classify runs in kernel. User-mode can **add filters** that point at a registered callout; it cannot implement the classify function itself.

### D. WFP connect redirect + local proxy + `IP_UNICAST_IF` — **chosen**

Matches Microsoft’s documented bind/connect redirection model:

- Filter on `FWPM_CONDITION_ALE_APP_ID` (the executable that creates the socket).
- Callout rewrites the connect to a local proxy.
- Proxy learns original destination via redirect context/records.
- Outbound socket: `IP_UNICAST_IF` + owned high-metric tunnel default for reachability.
- Loop prevention: proxy PID excluded; `SIO_SET_WFP_CONNECTION_REDIRECT_RECORDS`.

This is true per-process for **TCP connect**. It does not parse HTTP/TLS/DNS in kernel.

### E. Full packet-inspecting WFP driver — rejected

Out of scope. Kernel must stay small.

## OpenVPN strategy

Do not rewrite the user’s `.ovpn`. Runtime argv:

- `--route-nopull` — ignore pulled routes/DNS/redirect-gateway (profile may still *contain* those lines; they must not win).
- `--management 127.0.0.1 <ephemeral> <password-file>` — localhost only; password file ACL’d to SYSTEM/Administrators.
- `--disable-dco` only when the user enables **Compatibility mode**. DCO previously broke coexistence with another VPN on a real machine; it is **off by default** and explained in the UI.

Credentials: not stored in config or logs. Use an `auth-user-pass` file the user already has, or OpenVPN’s own prompt (hidden-window service cannot show a TTY; Test Center warns).

## Transport enablement route (MVP exception to V0)

Owned `0.0.0.0/0` on the **VPN interface**, metric **9000**, next hop = OpenVPN `route-gateway` / TUN peer / guessed `.1`.

Unbound sockets still prefer Direct (typical metric 25–50). Proxy sockets bound to the VPN index use the tunnel path.

Must never be confused with OpenVPN `redirect-gateway def1`.

## IPv6

A v4-only tunnel plus happy-eyeballs will leak Application rules via Direct IPv6. Default policy **blocks** IPv6 connect for VPN-routed APP_IDs at `ALE_AUTH_CONNECT_V6` (user-mode **block**, no callout). UI can choose leaky Allow Direct.

## UDP / QUIC

Transparent UDP per-process needs a different data path (datagram proxy / WFP stream is TCP). MVP: TCP only. QUIC on UDP/443 will bypass the TCP proxy; optional future: block UDP/443 for those APP_IDs to force TCP. Not enabled by default; not implemented as a silent global UDP kill.

## Work VPN coexistence

Personal OpenVPN must not install a preferred default. Corporate VPN routes stay untouched. Emergency Restore must not `route -f` or reset the stack.

## What is proven without the driver

Automated tests prove: rule matching, DIRECT vs VPN precedence, no parent inheritance, destination-global Domain/CIDR planning, route reconcile, config/crash-state, OpenVPN log parsing, **SOCKS proxy byte relay**.

Real-machine Test Center can prove: Direct HTTP, VPN-bound socket (after connect + transport route), OpenVPN without stealing a *low-metric* default.

Transparent `Cursor.exe` vs `git.exe` isolation is proven only with the callout **loaded** plus MANUAL_TEST_PLAN tests 3–6.
