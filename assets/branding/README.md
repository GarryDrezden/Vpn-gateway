# VPN Route branding assets

| File | Use |
|------|-----|
| `vpn-route-icon-source.png` | Standalone app icon (A). Source for ICO generation. |
| `vpn-route-icon.ico` | Production multi-size Windows ICO (generated). |
| `vpn-route-icon-tight-256.png` | QA preview after tight crop (generated). |
| `vpn-route-lockup.png` | Horizontal brand lockup (B). Docs/About only. |
| `vpn-route-wordmark.png` | Wordmark only (C). Docs/About only. |

Regenerate ICO:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\generate-app-icon.ps1
```
