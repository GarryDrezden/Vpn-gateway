# VPN Route desktop/service versioning (RC)

Canonical values live in **`Directory.Build.props`** at the repo root:

- `VpnRouteProductVersion` = `1.0.0` (public target, not a fake `1.0.14` release)
- `VpnRouteReleaseChannel` = `RC`
- `VpnRouteReleaseRevision` = `14`

Runtime API: `SelectiveVpnRouter.Core.ProductVersionInfo` and `ProductReleaseSnapshot` on `ServiceSnapshot.ProductRelease`.

Installer scripts read the same props via `scripts/_product-version.ps1`.

Keep in sync with **ext-vpn-route** `version/product-version.json` when bumping RC. See that repo’s `docs/product-version-rc.md` for the full RC → **v1.0.0** release checklist.
