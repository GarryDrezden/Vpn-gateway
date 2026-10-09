# Browser routing state in the Service

**Phase 5 status:** integrated and **FULL PASS** in `ext-vpn-route` (automated + Yandex manual acceptance against a live Service). This repo holds the authoritative store and read-only pipe; see acceptance details in `ext-vpn-route/docs/phase5-acceptance.md`.

**Browser Integration Port v1 — Slice 8 ACCEPTED:** Integration API v1 manifest, explicit loopback SOCKS5 browser proxy, VPN egress readiness, client heartbeat/stale semantics, VPN-bound DNS (OpenVPN PUSH), fail-closed browser routing, MV3 alarm-driven endpoint recovery in `ext-vpn-route`, real Yandex acceptance. Release baseline **807/807 PASS**.

**Slice 9A — DONE (committed, not deployed):** Service-side write methods `upsertRule`, `deleteRule`, `resetRules` with optimistic `expectedRevision` on this pipe. Capability `browserRoutingWrite` in Integration API manifest.

**Slice 9B — NEXT:** Native Host bridge and extension write client (`ext-vpn-route`); no rule-editor UI until Slice 9C.

**Runtime manifest (read):** `getManifest` publishes `integrationApiVersion` (`1`), `serviceVersion`, sorted `capabilities`, runtime `browserProxy`, `vpnEgress`, and `browserClient`. Contract fixture: `tests/contracts/browser-routing-v1/integration-manifest-v1.example.json`.

The Service is the **only source of truth** for browser routing: which hostnames the browser sends through the VPN.

- The VPN Route browser extension (repository `ext-vpn-route`) is a read-only consumer and owns PAC application.
- Its Native Messaging host is a stateless bridge.

Contracts live in `ext-vpn-route`:

| Document | Contents |
| --- | --- |
| `docs/browser-routing-contract-v1.md` | `BrowserRoutingStateV1`: schema, validation, 10000-rule limit |
| `docs/service-ipc-browser-routing-v1.md` | this pipe: framing, ACL, methods, errors, paging |
| `docs/phase5-service-integration.md` | end-to-end chain, lineage policy, tests |

Shared golden vectors: `tests/contracts` (copied from `ext-vpn-route/contracts/browser-routing-v1`).

```text
browser extension → NativeHost.exe → \\.\pipe\SelectiveVpnRouter.BrowserRouting → Service
                                                                                    └── BrowserRoutingStateStore
                                                                                        %ProgramData%\SelectiveVpnRouter\browser-routing-state.json
```

## Ownership

| Concern | Owner |
| --- | --- |
| rules, defaultRoute | Service (`BrowserRoutingStateStore`) |
| `stateGeneration` (UUID), `revision` | Service only; persisted, survive restart |
| explicit browser proxy readiness | Service runtime (`RuntimeBrowserProxyReadiness`); loopback SOCKS may be **Ready** while VPN is down |
| assembling pages, validation, lineage decisions, PAC | extension |
| relay of exactly two read-only calls | Native host |

`stateGeneration` changes only on `Reset()` (explicit recovery or destructive migration) or on the one-time migration of a legacy document. `revision` grows by one per `Update()` within a generation.

## Code

| File | Responsibility |
| --- | --- |
| `Core/BrowserRouting/BrowserRoutingModel.cs` | contract constants, state and rule records, generation format |
| `Core/BrowserRouting/BrowserRoutingValidator.cs` | C# port of the v1 validator (golden-vector parity with JS) |
| `Core/BrowserRouting/BrowserRoutingStateStore.cs` | persistence, initial state, restart, corruption handling |
| `Core/BrowserRouting/BrowserRoutingSnapshot.cs` | immutable snapshot, pre-serialized rules, page cursor |
| `Core/BrowserRouting/BrowserRoutingIpcProtocol.cs` | request parsing, dispatcher for `getManifest` and `getPage` |
| `Core/BrowserRouting/BrowserRoutingPipeServer.cs` | named pipe, DACL, framing, timeouts |
| `Service/BrowserRoutingPipeHost.cs` | hosted service: loads the store, runs the pipe server |

The pipe is registered in `Program.cs` with `AddHostedService<BrowserRoutingPipeHost>()`. It is independent of the existing control pipe `\\.\pipe\SelectiveVpnRouter` and of VPN state. Loading or serving browser state never connects, disconnects or reconfigures OpenVPN, routes, WFP or the driver.

## Persistence

The store writes:

- `browser-routing-state.json`;
- `browser-routing-state.bak.json`, the previous version;
- `browser-routing-state.json.tmp`, used while a write is in progress.

| Situation | Result |
| --- | --- |
| no file, no backup | create schemaVersion 1, new generation, revision 0, Direct, `rules: []`, persist, serve |
| valid file | serve unchanged (generation and revision preserved) |
| legacy document without generation | migrate once: keep revision and rules, assign a new generation |
| corrupt / invalid / unsupported / unreadable | **Unavailable**; clients get `browser_state_unavailable`; the file is not overwritten |
| primary missing, backup present | Unavailable (`state_missing_backup_present`); never silently re-created |
| write fails | old state kept in memory and on disk |

Writes are atomic: temp file, flush to disk, then `File.Replace` with a backup.

Nothing in the Service ever turns a broken state into an empty Direct state. Only an explicit store `Reset()` (new generation) does that for corruption recovery.

### Write API (Slice 9A — Service only, not deployed)

| Method | Params | Success result | Errors |
| --- | --- | --- | --- |
| `upsertRule` | `expectedRevision`, `rule` | `stateGeneration`, `revision`, `defaultRoute`, `ruleCount` | `revision_conflict` (+ `currentRevision`), `validation_failed`, `persistence_failed`, `browser_state_unavailable` |
| `deleteRule` | `expectedRevision`, `id` | same metadata | `not_found`, `revision_conflict`, … |
| `resetRules` | `expectedRevision` | clears rules, `defaultRoute: Direct`, revision +1 unless already empty (idempotent) | `revision_conflict`, … |

`expectedRevision` must match the current revision or the Service performs **no** mutation. Normal writes keep `stateGeneration` unchanged and bump `revision` by one after atomic persist (`browser-routing-state.json` temp + replace). Capability: `browserRoutingWrite`.

## Pipe security

The browser state is readable by local interactive users only. It is not writable through the pipe.

| Property | Value |
| --- | --- |
| name | `SelectiveVpnRouter.BrowserRouting`, `FirstPipeInstance` (refuses to share a squatted name) |
| DACL | protected (no inheritance); SYSTEM, Administrators, creator: FullControl; Interactive: ReadWrite; Network: Deny |
| not granted | Everyone, Authenticated Users, Users |
| transport | named pipe only; no TCP, no network port |
| methods | `getManifest`, `getPage` (read); **Slice 9A:** `upsertRule`, `deleteRule`, `resetRules` (write, `expectedRevision`) |
| limits | request ≤ 4 KiB; response ≤ 512 KiB; 5 s per client |
| errors | bare `{code}`; no exception text, paths or rule data |
| logs | method plus outcome only (`browser-routing getManifest: ok`) |

The client (Native host) connects with `TokenImpersonationLevel.Identification` and refuses a pipe whose owner is not SYSTEM, Administrators or the current user.

### Why a separate pipe

The existing control pipe:

- grants ReadWrite to Authenticated Users;
- has no per-method authorization;
- exposes connect, disconnect, config and diagnostics commands;
- can return exception messages.

Extending it would give a browser-launched process access to control commands. The separate pipe has a fixed read-only surface and a narrower DACL.

## Tests

`tests/SelectiveVpnRouter.BrowserRouting.Tests` (xUnit, offline, temp directories only) covers:

- the validator with golden vectors;
- store lifecycle (initial state, restart, corruption, legacy migration, atomic write failure);
- snapshot paging, including 10000 worst-case rules within the budget;
- dispatcher;
- the real pipe: DACL, `FirstPipeInstance`, framing limits, timeouts;
- source guards: no sockets, listeners or process launch; no Everyone, AuthUsers or Users SIDs; no VPN control calls; exactly two dispatcher methods;
- integration manifest fields, heartbeat params, client tracker TTL (120s), runtime `vpnEgress` / `browserClient` / `browserProxy` (no revision impact).

`tests/SelectiveVpnRouter.BrowserRouting.TestHost` is a console host for cross-process E2E from `ext-vpn-route` (`npm run test:e2e`):

- it runs the production store, dispatcher and pipe server on a temp store and a private `SelectiveVpnRouter.BrowserRouting.Test.<hex>` pipe;
- it only accepts that test name prefix.

```text
dotnet test SelectiveVpnRouter.sln -c Release
```

## Integration manifest (Slice 5)

| Field | Semantics |
| --- | --- |
| `integrationApiVersion` | Major-only integer (`1`); breaking changes → `2` |
| `serviceVersion` | Informational assembly version; not a compat gate |
| `capabilities` | Implemented features only (`browserRoutingState`, `browserExplicitSocks`, `vpnEgressReadiness`, `browserClientHeartbeat`) |
| `browserProxy` | Runtime loopback SOCKS from Slice 4; **Ready** independent of VPN |
| `vpnEgress` | Live `IVpnTunnelEgressReadiness` on RouterEngine; no cached mirror state |
| `browserClient` | In-memory last contact (`NeverSeen` / `RecentlySeen` / `Stale`, TTL **120s**) |

**Heartbeat:** `getManifest` without `params` remains valid (bootstrap / Phase 5). Optional `params.client` with `extensionVersion` and `nativeHostVersion` updates the tracker only after strict validation. Bootstrap without params does **not** touch `browserClient`. Runtime fields never change `stateGeneration` or `revision`. Dynamic `browserProxy.endpoint` after listener restart does not bump revision (PAC reapply is the extension’s job in Slice 6).

## Not implemented

- editing browser rules (UI or IPC) — slice 9 / separate spec;
- push notifications; the extension polls via Native Messaging;
- `browserRuleWrite` capability.

## Yandex Browser (application vs browser path)

Application routing for `yandex.exe` remains **Direct** (no whole-browser WFP/VPN redirect). Per-domain VPN in the browser uses the extension PAC → loopback SOCKS path only (see Integration Port spec).
