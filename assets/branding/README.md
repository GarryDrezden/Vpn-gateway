# VPN Route branding assets

## Tracked (source of truth)

| File | Use |
|------|-----|
| `vpn-route-icon-source.png` | Windows app icon master. Input for ICO generation. |
| `vpn-route-lockup.png` | Horizontal brand lockup. Docs/About only. |
| `vpn-route-wordmark.png` | Wordmark only. Docs/About only. |

Do not commit generated ICO/PNG previews in this folder.

## Generated (build output — not source of truth)

Produced by `scripts/generate-app-icon.ps1` into **`artifacts/branding/`** (gitignored via `artifacts/`):

| File | Use |
|------|-----|
| `artifacts/branding/vpn-route-icon.ico` | Multi-size Windows ICO for exe embed, WPF, shortcuts, publish sidecar |
| `artifacts/branding/vpn-route-icon-tight-256.png` | QA preview after tight crop |

MSBuild copies the generated ICO into the App project intermediate output for `ApplicationIcon`, WPF resources, and `CopyToOutputDirectory`.

`-SkipIfUpToDate` (build/publish) skips regeneration only when **both** generated outputs exist and are newer than every generation input: `vpn-route-icon-source.png`, `scripts/generate-app-icon.ps1`, and `scripts/branding-asset-paths.ps1`.

Publish/update/install may copy the generated ICO into live publish directories and shortcuts; they must **not** write back into `assets/branding/` or `src/SelectiveVpnRouter.App/`.

## Regenerate manually (optional)

```powershell
powershell -ExecutionPolicy Bypass -File scripts\generate-app-icon.ps1
```

## Future (not Phase 0)

Extension toolbar icons and tray status variants will get their own pipeline in later design prompts; they are not part of the Windows app ICO generator yet.
