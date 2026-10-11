# Browser Integration Port v1 — Design Spec

**Status:** Approved (2026-10-05, clarifications applied)
**Date:** 2026-10-05
**Source of truth:** `E:\Работа\OSPanel\domains\vpn-gateway` (current main)
**Reference / donor only:** `vpn-gateway-phase6-browser-proxy` (frozen; no feature development)
**Extension consumer:** `ext-vpn-route` (depends on contract, not on a specific app commit)

### Document status (maintenance note, 2026-10-11)

- Browser Integration Port v1 design originated with slices 0–8.
- **Slice 9 write support is now shipped and accepted** (see shipped code and current protocol documentation).
- **phase6 donor** references in this document are **historical implementation context**, not current porting instructions.
- **Current runtime, API, and protocol source of truth:** shipped product code and the active protocol/integration docs in each repository.
- **API/protocol version remains v1** for Browser Integration Port.

---

## 1. Purpose and success criteria

### 1.1 Goal

Add a **stable, versioned Browser Integration Port** to the current VPN Route application so the browser extension can integrate via a **contract**, while VPN Route and the extension evolve independently.

VPN Route remains owner of:

- OpenVPN profiles / connect / disconnect
- VPN adapter and RouterEngine
- WFP, driver, VPN-bound TCP egress
- Browser Explicit SOCKS proxy (loopback)
- Authoritative browser routing **state** (read path in v1)

The extension remains owner of:

- MV3 UI, domain rules UX
- `chrome.proxy` / PAC
- Native Messaging bridge
- Per-domain DIRECT vs local SOCKS routing

### 1.2 Success criteria (v1)

1. Service starts → Browser Explicit SOCKS listener → **Ready** on loopback (dynamic port).
2. VPN disconnected → `browserProxy: Ready`, `vpnEgress: Unavailable` → SOCKS CONNECT for VPN routes **fail-closed** (no DIRECT).
3. VPN connected → `vpnEgress: Ready` → SOCKS IPv4 and DOMAIN (VPN DNS) succeed through tunnel.
4. VPN disconnect / reconnect → listener stays **Ready**; egress tracks tunnel; no mandatory listener restart on reconnect.
5. Service restart → listener returns **Ready**; manifest publishes new endpoint if port changed.
6. No extension installed → core VPN Route behavior unchanged.
7. Integration API major mismatch → clear **incompatible** status (not silent failure).
8. Non-browser application routing regression suite remains **PASS**.
9. **App UI** shows browser integration observability (mandatory v1).

### 1.3 Milestone definition

**Slices 1–8** deliver **Browser Integration Port v1 runtime complete** (SOCKS, egress, handshake, extension consumer, App observability, acceptance tests).

That milestone is **not** the full end-user browser-routing product: rules remain **read-only** over IPC until **Slice 9** (or a separate approved design) adds **`browserRuleWrite`** with `upsertRule` / `deleteRule` / `resetRules` and `expectedRevision`.

### 1.4 Explicit non-goals (v1 runtime)

- Browser routing **write** API — **Slice 9 / separate spec** (see §1.3).
- Using phase6 worktree as application source or deploy target.
- Routing **Yandex Browser (`yandex.exe`)** through VPN at application/WFP layer.
- Persisting extension heartbeat or changing routing revision/generation via heartbeat.

---

## 2. Repository and porting policy

### 2.1 Source of truth

All product code, build, deploy, and documentation for VPN Route live in:

`E:\Работа\OSPanel\domains\vpn-gateway`

Current App, RouterEngine, portable subsystem, driver scripts, and UI implementations in main **must not** be replaced wholesale by phase6 copies.

### 2.2 Donor reference

`E:\Работа\OSPanel\domains\vpn-gateway-phase6-browser-proxy` may be used **only** to reference or surgically port:

- Browser Explicit SOCKS proxy and lifecycle
- VPN-bound TCP egress and tunnel readiness interfaces
- VPN-interface DNS for SOCKS DOMAIN
- Browser routing IPC/runtime wiring patterns
- Related automated tests
- Useful deployment/package guards (cherry-pick into main scripts when appropriate)

### 2.3 Forbidden porting actions

**Prohibited:**

- Replacing main `Program.cs`, `RouterEngine` (entire type), App UI project, or portable subsystem **entirely** with phase6 versions.
- Rolling back main to Phase 5/6 branch baselines.
- Merging phase6 worktree as the ongoing integration branch.

**Required:**

- **Surgical** additions: new files, new partials, targeted DI registration, manifest extensions, UI section, tests.
- Follow existing main patterns for logging, DI, IPC, and RouterEngine lifecycle.

---

## 3. Traffic and routing model

### 3.1 Application routing (unchanged)

Yandex Browser as an **application** in VPN Route routing rules remains **Direct**. The whole `yandex.exe` process must **not** be WFP-redirected to the transparent app proxy or VPN by default.

### 3.2 Browser integration path

Only domains selected via extension rules use VPN in the browser:

```text
Yandex Browser (or other Chromium)
    ↓
Extension PAC (chrome.proxy)
    ├── ordinary domain → DIRECT
    └── VPN domain
            ↓
       SOCKS5 127.0.0.1:<dynamic port>
            ↓
       VPN Route — Browser Explicit SOCKS (Service)
            ↓
       RouterEngine / VPN adapter (authoritative tunnel state)
            ↓
       VPN
```

### 3.3 Fail-closed

When VPN egress is unavailable, SOCKS must **not** fall back to system default route, system DNS, or DIRECT connect. Connection attempts for VPN-routed browser traffic fail closed.

---

## 4. Current architecture (main) — baseline

```text
SelectiveVpnRouter.App (WPF)
        │
        │  \\.\pipe\SelectiveVpnRouter  (control / status IPC)
        ▼
SelectiveVpnRouter.Service — RouterEngine
        ├── OpenVPN, owned routes, WFP, driver
        ├── TransparentTcpProxy (per-app WFP redirect path)
        └── BrowserRoutingPipeHost
                └── \\.\pipe\SelectiveVpnRouter.BrowserRouting
                        └── BrowserRoutingStateStore (Phase 5 — implemented)

Extension → NativeHost → BrowserRouting pipe (read-only today)
```

**Gap in main today:** Phase 5 state + IPC exist; **`browserProxy` is always `Unavailable`** (`UnavailableBrowserProxyReadiness`). No Browser Explicit SOCKS, no `vpnEgress` in handshake, no App UI block.

---

## 5. Browser Integration Port v1 — logical architecture

The **Browser Integration Port** is not a second TCP API. It is:

1. Existing read-only pipe `\\.\pipe\SelectiveVpnRouter.BrowserRouting`
2. Extended **`getManifest`** result (integration fields)
3. Optional **`getManifest` request params** for client heartbeat (v1)
4. Runtime services inside **SelectiveVpnRouter.Service**

```text
┌─────────────────────────────────────────────────────────────┐
│ Browser Integration Port v1 (logical)                        │
├─────────────────────────────────────────────────────────────┤
│ Transport IPC envelope          request "version": 1         │
│ Integration contract            integrationApiVersion: 1     │
│ Methods (v1)                    getManifest, getPage       │
│ State                           BrowserRoutingStateV1        │
│ Runtime                         browserExplicitSocks         │
│                                 vpnEgressReadiness           │
│ Client observability            browserClient (in-memory)    │
└─────────────────────────────────────────────────────────────┘
```

**Native Host** (`ext-vpn-route`): dumb, version-aware bridge — no VPN control, no rule authority, no DIRECT fallback.

---

## 6. Versioning and compatibility

### 6.1 Three version concepts

| Concept | Field / location | Used for |
|--------|-------------------|----------|
| IPC envelope | `"version": 1` on request/response | Framing, allowed methods |
| Integration API | `integrationApiVersion` in `getManifest` result | Extension ↔ Service compatibility |
| Product | `serviceVersion` (informational) | Display only; **not** compat gate |

Native Messaging `protocolVersion` in `ext-vpn-route` remains independent (Phase 5 model).

### 6.2 `integrationApiVersion` (major-only integer)

- Field type: **integer major only**, e.g. `"integrationApiVersion": 1`.
- **No** fractional or semver meaning in this field (no conceptual “1.1”).
- All **additive** behavior within the same major is gated by **`capabilities`** string tokens, for example:
  - `browserRoutingState`
  - `browserExplicitSocks`
  - `vpnEgressReadiness`
  - `browserClientHeartbeat`
  - (future) `browserRuleWrite`, `browserDiagnostics`, `browserTrafficStats`
- **`integrationApiVersion` becomes `2` only on a breaking contract change** (new major).

### 6.3 Compatibility rules

1. VPN Route may update without a synchronized extension release.
2. Extension / Native Host perform handshake via `getManifest`.
3. Compatibility is decided on **`integrationApiVersion` integer major**, not whole app version.
4. If Service major **>** extension supported major → user-visible **Browser Integration incompatible** (Service API X, extension supports Y). No silent PAC apply with wrong semantics.
5. Within integration API v1, **additive** JSON fields are backward-compatible; extension ignores unknown fields.
6. Extension **capability-detects** optional features (e.g. no `browserRuleWrite` → read-only state still works; no `browserClientHeartbeat` → never send `params.client`).

### 6.4 Extension expectations

- Declare supported integration API major (e.g. `1`).
- Validate manifest shape including new fields when present.
- Apply PAC only when lineage + proxy readiness policies allow (existing Phase 5 coordinator rules, extended for `vpnEgress` awareness in UI/diagnostics).
- Treat **`browserProxy.endpoint` changes** as an independent trigger for PAC reapply (see §7.6); do not assume endpoint stability across Service restarts when `stateGeneration` / `revision` are unchanged.

---

## 7. IPC contract (service ↔ native host)

### 7.1 Endpoint (unchanged)

| Property | Value |
|----------|--------|
| Pipe | `\\.\pipe\SelectiveVpnRouter.BrowserRouting` |
| Methods (v1) | `getManifest`, `getPage` only |
| Security | Existing Phase 5 DACL and client trust checks |

Authoritative protocol details remain in `ext-vpn-route/docs/service-ipc-browser-routing-v1.md`; this spec defines **additive** manifest and request behavior.

### 7.2 Request: `getManifest` without params (backward compatible)

```json
{ "version": 1, "id": "550e8400-e29b-41d4-a716-446655440000", "method": "getManifest" }
```

Behavior identical to today for state paging identity; result includes new fields (clients must tolerate unknown fields; extension validates known subset).

### 7.3 Heartbeat bootstrap compatibility (Native Host)

**Phase 5 strict Service** may reject **`params` on `getManifest`** (manifest request allowlist = `version`, `id`, `method` only).

**Native Host protocol:**

1. **First** `getManifest` in a session (and after any integration reset) is **always without `params`**.
2. After a successful manifest response, the host/extension inspect:
   - `integrationApiVersion == 1` (supported major), and
   - `capabilities` contains **`browserClientHeartbeat`**.
3. **Only then** may subsequent `getManifest` calls include optional heartbeat `params`.

If `browserClientHeartbeat` is absent, the client **never** sends `params` (remains compatible with strict Phase 5 parsers).

### 7.4 Request: `getManifest` with optional client heartbeat (when allowed)

When capability `browserClientHeartbeat` is present:

```json
{
  "version": 1,
  "id": "req-1",
  "method": "getManifest",
  "params": {
    "client": {
      "extensionVersion": "1.2.3",
      "nativeHostVersion": "1.0.0"
    }
  }
}
```

**Rules:**

- `params` omitted → always valid.
- `params.client` updates **in-memory** `lastSeenUtc` and `browserClient.status` only.
- Heartbeat **must not** change `stateGeneration`, `revision`, rules, or persist any routing document.
- Invalid extra keys in `params` → `invalid_request` (strict allowlist).
- Service **must** accept manifest **without** `params` even after heartbeat capability is enabled (backward compatible).

**Service IPC evolution:** until Integration Port is deployed, strict Phase 5 behavior remains; after Slice 5, dispatcher allowlist for `getManifest` may include optional `params` when implementing heartbeat — old clients unaffected.

### 7.5 Response: `getManifest` result shape (v1)

Existing fields preserved. Additive fields:

```json
{
  "schemaVersion": 1,
  "integrationApiVersion": 1,
  "serviceVersion": "0.x.y",
  "capabilities": [
    "browserRoutingState",
    "browserExplicitSocks",
    "vpnEgressReadiness",
    "browserClientHeartbeat"
  ],
  "stateGeneration": "75f5c435-6ac5-45a5-876b-042a6376635e",
  "revision": 0,
  "defaultRoute": "Direct",
  "ruleCount": 0,
  "pageBudgetBytes": 520192,
  "browserProxy": {
    "status": "Ready",
    "endpoint": { "host": "127.0.0.1", "port": 54321 }
  },
  "vpnEgress": {
    "status": "Ready",
    "interfaceIndex": 42,
    "interfaceName": "OpenVPN TAP-Windows6"
  },
  "browserClient": {
    "status": "NeverSeen",
    "lastSeenUtc": null
  }
}
```

**Field semantics:**

| Field | Semantics |
|-------|-----------|
| `browserProxy.status` | `Ready` iff loopback SOCKS listener active and endpoint valid; else `Unavailable` / null endpoint |
| `vpnEgress.status` | `Ready` iff authoritative RouterEngine reports connected tunnel + valid interface index; else `Unavailable` |
| `vpnEgress.interfaceIndex` / `interfaceName` | Set only when Ready; otherwise null |
| `browserClient.status` | `NeverSeen` \| `RecentlySeen` \| `Stale` — **last-contact telemetry**, not live connection state (see §8.7) |
| `browserClient.lastSeenUtc` | ISO-8601 UTC from last accepted `params.client`; in-memory only |
| `capabilities` | Stable string tokens; additive features within major v1 |
| `ruleCount` | From authoritative store; 0..10000 |

`getPage` unchanged except it continues to require matching `{stateGeneration, revision}`.

### 7.6 Dynamic `browserProxy.endpoint` (runtime, not revision)

- `browserProxy.endpoint` (`host`, `port`) is **runtime state** of the Browser Explicit SOCKS listener.
- It is **not** part of `BrowserRoutingStateV1` and **does not** bump `revision` or change `stateGeneration`.
- **Service restart** (or listener restart) may change port, e.g. `127.0.0.1:oldPort` → `127.0.0.1:newPort`, while `stateGeneration` and `revision` stay the same.

**Extension coordinator (mandatory):**

- Treat a change in Ready `browserProxy.endpoint` (host/port) as an **independent trigger** for PAC reapply, even when routing identity `{stateGeneration, revision}` is unchanged.
- **Must not** leave a stale SOCKS port in an active PAC.
- VPN routes in PAC remain **fail-closed**: `SOCKS5 127.0.0.1:<port>` for VPN domains — **no** `; DIRECT` fallback on those routes.

### 7.7 Future capabilities (not v1 runtime)

Documented for forward compatibility only:

- `browserRuleWrite`
- `browserDiagnostics`
- `browserTrafficStats`

Must not be assumed by extension v1; gate on capability presence when implemented.

---

## 8. Runtime components (Service)

### 8.1 Browser Explicit SOCKS

- Hosted background service starts listener on **127.0.0.1:0** (dynamic port).
- **Independent** of VPN Connect/Disconnect for listener lifecycle (Ready unless internal failure).
- Survives internal listener failure via restart loop (reference: phase6 `BrowserExplicitProxyLifecycle`).
- Publishes current port through `IBrowserProxyReadiness` (`RuntimeBrowserProxyReadiness` pattern).
- Max concurrency and SOCKS protocol limits as in phase6 reference implementation unless main standards differ after review.

### 8.2 VPN egress

- **`IVpnTunnelEgressReadiness`** implemented by **`RouterEngine`** (partial class added to main) — **single authoritative source** for tunnel connected state and interface index.
- **`IVpnTcpEgress` / `VpnBoundTcpEgress`** uses readiness + **`VpnInterfaceDnsResolver`** for DOMAIN connects.
- No mirror state that can diverge from RouterEngine.

### 8.3 DNS (SOCKS DOMAIN)

- Resolve hostnames **only** via VPN adapter DNS (UDP, `IP_UNICAST_IF` on tunnel interface).
- No system resolver fallback, no default-adapter DNS, no DIRECT retry after failure.
- Reference implementation: phase6 `VpnInterfaceDnsResolver`; adapt to current Network stack APIs in main.

### 8.4 Browser routing pipe host

- Replace stub `UnavailableBrowserProxyReadiness` with runtime readiness wired to SOCKS host.
- Inject egress readiness for manifest `vpnEgress` section.
- Maintain independence from OpenVPN connect/disconnect side effects in **state store** (loading/serving rules must not connect VPN).

### 8.5 Service DI (surgical)

Add registrations only for browser integration types (reference phase6 `Program.cs` **pattern**, not wholesale replace):

- `RuntimeBrowserProxyReadiness` / `IBrowserProxyReadiness`
- `BrowserExplicitSocksProxy`
- `IVpnTcpEgress` → `VpnBoundTcpEgress`
- `IVpnTunnelEgressReadiness` → `RouterEngine`
- Hosted: `BrowserExplicitProxyHost` (+ lifecycle helper)
- Existing: `BrowserRoutingPipeHost`

Use `BackgroundServiceExceptionBehavior.Ignore` for explicit proxy host if required so one failed background service does not tear down entire Service (phase6 lesson).

### 8.6 Extension presence (in-memory)

- Track `lastSeenUtc` and derived `browserClient.status` when heartbeat `params.client` is accepted.
- Service startup **must not** depend on extension.
- VPN Route fully usable with zero browser extension installed.

### 8.7 `browserClient` semantics (API vs UI)

**API statuses** (only these three):

- `NeverSeen` — no accepted heartbeat since Service process start.
- `RecentlySeen` — last heartbeat within the **RecentlySeen TTL** (defined in implementation plan).
- `Stale` — last heartbeat older than TTL.

This is **not** a persistent “extension connected” session flag. Heartbeat proves **recent contact**, not an open channel.

**UI copy (App and extension diagnostics):**

- Prefer **last contact** / **recently active** wording.
- **Do not** imply permanent “Connected / Disconnected” like a VPN session.
- Example labels: «Расширение: контакт не было», «Недавно активно (… UTC)», «Давно не обращалось».

TTL **RecentlySeen → Stale** is specified in the implementation plan (not in this spec numerically).

---

## 9. Rules and state (v1)

### 9.1 Model

Continue **BrowserRoutingStateV1** (Phase 5):

- `stateGeneration`, `revision`, `defaultRoute`, `rules[]`
- Rule fields: `id`, `name`, `host`, `matchType`, `routeMode`, `enabled`, `source`, `notes`
- Priority: ExactHost → longest DomainAndSubdomains → defaultRoute
- RouteMode: VPN, Direct, Default

### 9.2 Persistence

Unchanged Phase 5 store behavior (`browser-routing-state.json`, corruption → `browser_state_unavailable`, no silent empty reset).

### 9.3 Write path

**Out of scope for v1 port.** No IPC write methods until a follow-on spec approves optimistic concurrency on `revision`.

---

## 10. App UI observability (mandatory v1)

### 10.1 Data source

App reads browser integration status via **main control IPC** (`\\.\pipe\SelectiveVpnRouter`), not by opening the browser routing pipe from the UI process.

Service aggregates into status/diagnostics snapshot, e.g. section **`browserIntegration`** mirroring manifest fields plus display-friendly strings.

### 10.2 UI block: «Браузерная маршрутизация»

Minimum v1 fields:

| UI label | Data |
|----------|------|
| Extension (last contact) | `browserClient.status` + `lastSeenUtc` — not “always connected” |
| Browser API | `integrationApiVersion` (e.g. v1) |
| Browser Proxy | Ready / Unavailable + `127.0.0.1:port` when Ready |
| VPN egress | Ready / Unavailable + interface index/name when Ready |
| Browser VPN rules | `ruleCount` when state available |
| Fail-closed hint | When proxy Ready and egress Unavailable: explicit copy that VPN browser routes **fail-closed** |

Optional later (not blocking v1): active SOCKS connections, last hostname, last error — requires Service counters.

### 10.3 VPN disconnected UX

User must see clearly:

- Browser Proxy: **Ready**
- VPN Egress: **Unavailable**
- VPN browser routes: **Fail-closed**

Not a generic “service broken” message.

---

## 11. Extension (`ext-vpn-route`) responsibilities

- Consume extended manifest; build `BrowserRoutingSnapshot` with `browserProxy` + integration fields validated separately from pure `BrowserRoutingStateV1`.
- Enforce integration API major compatibility before applying PAC.
- Heartbeat bootstrap: first `getManifest` **without** `params`; send `params.client` only if `browserClientHeartbeat` capability present (§7.3).
- On endpoint port change with stable revision, **reapply PAC** (§7.6).
- Capability-detect future features.
- Keep Native Host dumb: two read methods (+ optional params on manifest only).

Documentation updates (companion to this spec, separate repo):

- `docs/service-ipc-browser-routing-v1.md` — manifest extensions + optional params
- Snapshot validation in `src/extension/state/snapshot.js` (when implementing)

---

## 12. Phase6 → main mapping (implementation reference)

| Donor (phase6) | Main action |
|----------------|-------------|
| `Proxy/BrowserExplicit/*` | Port/adapt |
| `Network/VpnInterfaceDnsResolver.cs` | Port/adapt |
| `Core/BrowserRouting/RuntimeBrowserProxyReadiness.cs`, `IVpnTunnelEgressReadiness.cs` | Port/adapt |
| `Service/BrowserExplicitProxyHost.cs`, `BrowserExplicitProxyLifecycle.cs` | Port/adapt |
| `Service/RouterEngine.BrowserEgress.cs` | Adapt as **new partial** + hooks in existing connect/disconnect paths |
| `BrowserRoutingPipeHost` | **Merge** readiness/egress wiring only |
| `BrowserRouting` state/IPC core | **Reuse** main; diff if donor has test-only fixes |
| `Program.cs` (full) | **Do not replace** — surgical DI only |
| App UI from phase6 | **Do not port** — build block in current WPF |
| Portable/deploy scripts | Cherry-pick guards later if needed |
| BrowserRouting.Tests (lifecycle/egress) | Port tests into main test project |

---

## 13. Acceptance tests (minimum)

1. Service starts → browser proxy **Ready**.
2. VPN disconnected → proxy Ready, egress Unavailable → SOCKS CONNECT **fail-closed**.
3. VPN connected → proxy Ready, egress Ready → SOCKS IPv4 success.
4. SOCKS DOMAIN → DNS via VPN adapter only → success when VPN up.
5. VPN disconnect after traffic → listener stays Ready; CONNECT **fail-closed**.
6. VPN reconnect → listener restart **not** required; CONNECT success.
7. Service restart → listener Ready; manifest endpoint updated if port changed; extension **reapplies PAC** to new port without revision change.
8. No extension → VPN Route normal operation.
9. Integration API major mismatch → clear incompatible error path (Service + extension behavior).
10. Existing non-browser routing regression suite **PASS**.

---

## 14. Implementation slices (approved order)

| Slice | Deliverable |
|-------|-------------|
| **0** | This spec + sync ext-vpn-route IPC doc when implementing |
| **1** | `IVpnTunnelEgressReadiness` + RouterEngine partial + lifecycle hooks |
| **2** | `VpnInterfaceDnsResolver` + tests |
| **3** | `BrowserExplicit/*` + `VpnBoundTcpEgress` + unit tests |
| **4** | Runtime readiness, SOCKS host, lifecycle, surgical DI |
| **5** | Manifest extensions, heartbeat params, dispatcher tests |
| **6** | `ext-vpn-route` validation + compat messaging |
| **7** | App UI block via control IPC snapshot |
| **8** | Full acceptance list + regression |
| **9** | **Separate approved design:** `browserRuleWrite`, upsert/delete/reset, `expectedRevision` |

**Milestone:** Slices **1–8** = Integration Port v1 **runtime** complete. Slice **9** = full mutable browser-routing over IPC.

**No product code until the implementation plan (slices 0–8) is approved.**

---

## 15. Risks and mitigations

| Risk | Mitigation |
|------|------------|
| Accidental wholesale phase6 merge | This spec §2.3; code review checklist |
| Two proxies confused (transparent vs browser) | Document ports; tests assert distinct endpoints |
| Egress desync | Single RouterEngine readiness interface |
| DNS leak | Dedicated tests; no fallback paths in egress |
| Extension ahead/behind Service | `integrationApiVersion` major gate |
| UI reads wrong pipe | UI only via main IPC aggregate |
| Scope creep (write API) | Deferred slice 9 |

---

## 16. Deployment

- Production build of **vpn-gateway** includes browser integration; no separate phase6 publish tree.
- One source of truth for the application: `vpn-gateway` repo.
- Deploy/package completeness guards may be cherry-picked from phase6 into main scripts in a later maintenance slice (not part of integration port runtime).

---

## 17. Related documents

| Document | Location |
|----------|----------|
| Browser routing in Service (Phase 5) | `vpn-gateway/docs/browser-routing-service.md` |
| Service IPC v1 | `ext-vpn-route/docs/service-ipc-browser-routing-v1.md` |
| State contract v1 | `ext-vpn-route/docs/browser-routing-contract-v1.md` |
| End-to-end integration | `ext-vpn-route/docs/phase5-service-integration.md` |
| Architecture overview | `vpn-gateway/docs/ARCHITECTURE.md` |

---

## 18. Spec self-review (inline)

- [x] Heartbeat bootstrap: first manifest without params; `browserClientHeartbeat` gate (§7.3).
- [x] `integrationApiVersion` major-only; capabilities for additive features (§6.2).
- [x] `browserClient` = last-contact telemetry; UI wording (§8.7).
- [x] Dynamic endpoint + PAC reapply; fail-closed SOCKS (§7.6).
- [x] Milestone slices 1–8 vs slice 9 write API (§1.3, §14).
- [x] Porting prohibitions §2.3; Yandex Direct §3; UI §10.

---

## 19. Next step

Implementation plan: `docs/superpowers/plans/2026-10-05-browser-integration-port-v1-implementation-plan.md` (slices 0–8).

No product code until that plan is approved. No commit/tag/push unless explicitly requested.
