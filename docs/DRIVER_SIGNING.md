# Driver signing (development)

The GUI, Windows service, and `scripts\*.ps1` **never**:

- enable `TESTSIGNING`
- disable Secure Boot
- disable Memory Integrity / HVCI
- suspend BitLocker

If Windows refuses `SelectiveVpnCallout.sys`, you change boot policy **yourself**, then revert it.

## 1. Check Secure Boot

Elevated PowerShell:

```powershell
Confirm-SecureBootUEFI
```

Or Test Center → **Check Secure Boot**. Registry: `HKLM\SYSTEM\CurrentControlSet\Control\SecureBoot\State\UEFISecureBootEnabled`.

Secure Boot On + unsigned/test-signed kernel driver = load will fail. Turning Secure Boot off is a firmware/UEFI action, not something this app will do.

## 2. Check Memory Integrity / HVCI

Settings → Privacy & security → Windows Security → Device security → Core isolation → Memory integrity.

Or:

```powershell
Get-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity' -Name Enabled
```

Test Center → **Check HVCI**. HVCI On typically requires a WHQL/attestation-signed driver. Test-signed drivers often will not load.

## 3. Check TESTSIGNING (read-only)

```powershell
bcdedit /enum {current}
```

Look for `testsigning Yes`. Test Center → **Check test-signing state**. `scripts\check-driver.ps1` only prints this.

## 4. Create a test certificate (manual)

Elevated PowerShell, **your** choice to trust it locally:

```powershell
$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=SelectiveVpnRouter Test" -CertStoreLocation Cert:\LocalMachine\My
Export-Certificate -Cert $cert -FilePath $env:TEMP\svr-test.cer
Import-Certificate -FilePath $env:TEMP\svr-test.cer -CertStoreLocation Cert:\LocalMachine\Root
Import-Certificate -FilePath $env:TEMP\svr-test.cer -CertStoreLocation Cert:\LocalMachine\TrustedPublisher
```

Remove those certs when done (MMC Certificates, Local Computer).

## 5. Sign SYS (and CAT if you package INF)

After `scripts\build-driver.ps1` produces `artifacts\driver\Release\SelectiveVpnCallout.sys`:

```powershell
$sys = "E:\path\to\artifacts\driver\Release\SelectiveVpnCallout.sys"
$sha = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe" | Select-Object -Last 1
& $sha.FullName sign /v /s My /n "SelectiveVpnRouter Test" /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $sys
```

Optional catalog for `pnputil`:

```powershell
inf2cat /driver:<folder-with-inf-and-sys> /os:10_x64
signtool sign /v /s My /n "SelectiveVpnRouter Test" /fd SHA256 <file.cat>
```

This repo’s development path is `sc.exe create type= kernel` (INF/CAT optional).

## 6. Verify the signature

```powershell
Get-AuthenticodeSignature .\SelectiveVpnCallout.sys | Format-List *
```

`Valid` is required unless TESTSIGNING is already on.

## 7. What requires a reboot

| Action | Reboot? |
| --- | --- |
| `bcdedit /set testsigning on` | **Yes** |
| `bcdedit /set testsigning off` | **Yes** |
| Firmware Secure Boot change | **Yes** |
| Memory Integrity toggle | **Yes** |
| `sc start SelectiveVpnCallout` after a trusted signature | No |

Enable test signing **only on a throwaway/dev box**:

```powershell
bcdedit /set testsigning on
# reboot
```

## 8. Put the machine back

```powershell
bcdedit /set testsigning off
# reboot
```

Re-enable Secure Boot and Memory Integrity in firmware/Windows Security if you turned them off.

Uninstall the driver: `scripts\uninstall-driver.ps1`.

Delete the test certificates from Local Computer `\Root` and `\TrustedPublisher`.

`scripts\uninstall-clean.ps1` does not touch boot policy.

## 9. Production

Shipping a kernel callout requires an EV code-signing certificate and Microsoft attestation signing (Hardware Dev Center). This repository does not ship a production catalog.
