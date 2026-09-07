# Manual test plan

Automated tests cannot fully prove dual-VPN, Cursor Agent, or fail-open after reboot. Run these on the target Windows 10/11 x64 machine.

Prerequisites: .NET 10 runtime/SDK, OpenVPN Community, a known-good personal `.ovpn` (prefer the profile that already works with `disable-dco` if DCO previously broke the network), optional corporate VPN.

Record PASS / FAIL / notes for each.

## TEST 1 — No personal VPN

- Do not start Selective VPN Router (or leave VPN disconnected).
- Browser to a public site; ping or `Probe --http https://example.com`.
- **Expect:** Internet works normally.

## TEST 2 — Connect personal VPN with route-nopull

- Start Service elevated, App, Connect (Compatibility mode if needed).
- Test Center: Detect internet interfaces, Detect VPN interfaces, Connect VPN.
- Browse while personal VPN is up.
- **Expect:** Browser/Direct traffic still uses normal internet. VPN adapter is up. No new *low-metric* `0.0.0.0/0` or `0.0.0.0/1` on the personal tunnel. Owned high-metric (9000) VPN default may exist — that is for the proxy only.

## TEST 3 — Route Probe → VPN

- Add Application rule: `SelectiveVpnRouter.Probe.exe` → VPN.
- If driver loaded: `Probe --http https://api.ipify.org` (or configured endpoint). Live connections should show Probe, VPN, tunnel adapter.
- If driver **not** loaded: Test Center “Test application routing” = WARNING. Then `Probe --via-proxy 127.0.0.1:<proxyPort> --http https://api.ipify.org` to exercise the VPN-bound proxy. Do **not** call that transparent per-process.
- **Expect:** Probe egress via personal VPN (public IP / local address on tunnel).

## TEST 4 — Chrome (or any unlisted app) stays DIRECT

- With Probe (or Cursor) on VPN, use Chrome to the same public site.
- **Expect:** Chrome remains Direct (different local interface / public IP than Probe). Same destination IP must **not** drag Chrome into the VPN unless you added a Domain/IP rule.

## TEST 5 — Cursor.exe → VPN

- Application rule: full path to `Cursor.exe` → VPN. Driver must be loaded for transparent TCP.
- Use Cursor Agent / chat that hits Cursor cloud APIs.
- Live connections: process `Cursor.exe`, destinations such as `*.cursor.sh`, route VPN.
- **Expect:** Agent works through personal VPN. Do not maintain a manual Cursor IP list.

## TEST 6 — git.exe → DIRECT

- Application rule `git.exe` → DIRECT (or simply no VPN rule for git).
- From a terminal: `git fetch` / `git pull` against GitHub or work GitLab.
- **Expect:** git stays on work/Direct network even if Cursor has a VPN rule. If Cursor spawned git, policy is still git’s executable, not Cursor’s.

## TEST 7 — Work VPN + personal VPN

- Connect corporate VPN first, then personal OpenVPN via this app (`--route-nopull`).
- Work resources still reachable (git, RDP, internal sites).
- Probe/Cursor still follow Application rules.
- **Expect:** both functional. If the machine previously died without `disable-dco`, enable Compatibility mode.

## TEST 8 — Disconnect personal VPN

- Stop VPN in the app.
- **Expect:** Direct internet still works. Owned routes gone. Corporate VPN (if any) untouched.

## TEST 9 — Kill the service unexpectedly

- While connected, End Task `SelectiveVpnRouter.Service.exe` (or `sc stop` mid-flow).
- **Expect:** fail open — Direct internet works. Dynamic WFP session drops filters. Job should kill OpenVPN. If a high-metric owned default remains, restart the service once (startup crash cleanup) or press Emergency Restore.

## TEST 10 — Reboot

- Reboot without a clean Stop.
- **Expect:** no stale WFP policy breaking internet (dynamic session). If a persistent owned route survived (IP Helper routes are not dynamic), start the app/service once or Emergency Restore. Document if a route survived — that is a bug to fix in crash-state coverage.

## Extra — IPv6 leak

- VPN-routed app, tunnel without global IPv6, policy Block/Auto.
- **Expect:** app uses IPv4 through VPN or fails closed on v6, not silent Direct IPv6 to the same service.

## Extra — Emergency Restore

- Press the red button.
- **Expect:** personal OpenVPN stopped; owned routes gone; other adapters/routes remain.
