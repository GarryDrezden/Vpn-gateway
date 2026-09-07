# Security

The product requires elevation for routing, WFP, and OpenVPN. The **GUI does not need to stay elevated**.

## Trust boundary

- Privileged operations run in `SelectiveVpnRouter.Service`.
- IPC: named pipe `SelectiveVpnRouter`, byte mode, length-prefixed JSON.
- ACL: FullControl for Local System and Administrators; ReadWrite for Authenticated Users; **Deny** Network SID (no remote named-pipe clients).
- This is local-machine isolation, not a multi-user hardened service. A local non-admin can still *connect* to the pipe on a typical desktop — treat physical access as trusted, or tighten the ACL to a specific user SID in a later revision.

## OpenVPN management interface

Bound to `127.0.0.1`, ephemeral port, password file under `%ProgramData%\SelectiveVpnRouter\runtime\` with SYSTEM/Administrators ACL. Deleted on stop.

## Secrets

- VPN passwords, keys, and `.ovpn` private material are **not** copied into app config or diagnostic ZIP.
- Logs run through a token redactor (`password`, `-----BEGIN`, etc.).
- Do not commit `.ovpn`, `.key`, `.pem` (gitignored).

## Config locations

| Path | Contents |
| --- | --- |
| `%ProgramData%\SelectiveVpnRouter\` | Service config, logs, crash-state, runtime |
| `%LocalAppData%\SelectiveVpnRouter\` | UI prefs |

ProgramData should be writable by Administrators/SYSTEM. Do not grant Users write to `runtime\`.

## Driver

Device `\\.\SelectiveVpnCallout` is Administrators/SYSTEM only. Kernel code only redirects TCP connects; no payload inspection.

## Diagnostic ZIP

Includes version, OS, adapters, IPv4 route dump, rules, sanitized OpenVPN lines, service status. No private keys by design — still review before sharing (adapter names, hostnames).
