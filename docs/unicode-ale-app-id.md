# Unicode ALE_APP_ID routing

## Symptom

Applications installed under **Unicode paths** (Cyrillic usernames, non-ASCII folder names, e.g. Telegram under `%AppData%`) did **not** hit the WFP connect-redirect callout, while the same binary from an ASCII path worked.

Controlled reproduction: `SelectiveVpnRouter.Probe.exe` from `ProgramData\SVRProbeAscii` — PASS; the same probe copied under `ProgramData\VPN Route WFP Tests\Тест VPN Route` — FAIL (no classify/redirect), with VPN Route otherwise healthy.

## Root cause

`FWPM_CONDITION_ALE_APP_ID` compares the filter blob **byte-exact** (UTF-16LE, including NUL).

| Source | Example segment |
| --- | --- |
| `FwpmGetAppIdFromFileName0` (canonical) | `...\users\**В**ячеслав\...` (`U+0412`) |
| Runtime `FWPS_ALE_APP_ID` | `...\users\**в**ячеслав\...` (`U+0432`) |

Byte proof (representative): `Т` → UTF-16LE `22 04`; runtime `т` → `42 04` at the same index.

Lowercasing the **filesystem path before** `FwpmGetAppIdFromFileName0` does **not** help: the API restores canonical filesystem casing in the APP_ID string.

An **8.3 short-path** second filter is **not** a production fix for this mismatch (and is not used for default app identity anymore).

## Production algorithm

Implemented in `WfpAleAppIdBuilder` and used only from `WfpSession.InstallAppFilter`:

1. `FwpmGetAppIdFromFileName0(path)` → decode canonical APP_ID string from blob
2. `normalized = canonical.ToLowerInvariant()`
3. Build owned blob: UTF-16LE(`normalized` + `\0`)
4. Use that blob for `FWPM_CONDITION_ALE_APP_ID`
5. Free Fwpm blob with `FwpmFreeMemory0`; keep normalized blob alive through `FwpmFilterAdd0`, then dispose owned allocation

Default identity paths: **single long path** only (`WfpAppIdentity.GetIdentityPaths` Default mode).

## Regression (offline)

Infrastructure (no VPN):

```powershell
.\scripts\test-runtime-diagnostic-preflight.ps1
```

Expect: `RUNTIME DIAGNOSTIC PREFLIGHT: PASS`

After deploy and **VPN Route Connected** (callout loaded):

```powershell
.\scripts\run-wfp-appid-regression.ps1
```

Key acceptance signals:

| Diagnostic | Expect |
| --- | --- |
| `wfp-runtime-appid-normalization-matrix` | `counterExamples=0` |
| `wfp-runtime-appid-blob` | Unicode production-normalized `delta>0` |
| `wfp-probe-unicode-redirect` | Outcome PASS, `filtersOk=True`, VPN public IP |
| `wfp-telegram-appid-acceptance` | Real user `Telegram.exe`, filter installed |

Unit tests: `WfpAleAppIdBuilderTests`, `WfpProbeRedirectVerdictTests`, `WfpProductionAppIdInvariantTests`.

## Tag

Milestone tag: `unicode-appid-routing-pass-v1` (Unicode APP_ID routing verified end-to-end, including Telegram).