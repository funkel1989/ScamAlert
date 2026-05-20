# ScamAlert desktop installer (MSI)

Packages **ScamAlert.Broker** (Windows service `ScamAlertBroker`) and **ScamAlert.Tray** (shortcut in the all-users Startup folder).

## Build

Requires Windows and the [.NET 10 SDK](https://dotnet.microsoft.com/download). WiX is restored automatically via `WixToolset.Sdk` (NuGet).

The installer project is in the solution for editing but **not** built by default with `dotnet build ScamAlert.sln` (avoids RID conflicts with Broker). Always use the script below or build the `.wixproj` directly.

```powershell
.\scripts\build-desktop-installer.ps1
```

Output: `installer\ScamAlert.DesktopInstaller\bin\Release\ScamAlert-Desktop.msi` (name may vary slightly by WiX version).

## Install

1. Run the MSI elevated (per-machine install under `Program Files\ScamAlert`).
2. Confirm services: `sc query ScamAlertBroker` should show **RUNNING** (or start it).
3. Sign out/in (or reboot) so the Tray shortcut in Startup runs in the user session.
4. In the portal: **Devices → Pair PC** → run `scripts\configure-broker-from-pairing-code.ps1` on that machine (pairing UI inside the MSI is planned next).

## Layout

| Path | Purpose |
|------|---------|
| `Program Files\ScamAlert\Broker\` | Broker service binaries |
| `Program Files\ScamAlert\Tray\` | Tray UI binaries |
| `%ProgramData%\ScamAlert\` | `broker.appsettings.json` (written by pairing script) |

## Signing (production)

Beta builds can use unsigned MSIs (SmartScreen warning). For production, sign the MSI with your code-signing certificate:

```powershell
signtool sign /fd SHA256 /a "path\to\ScamAlert-Desktop.msi"
```

## Azure blob upload (after deploy)

Upload the MSI to the storage account `installers` container and set `Web__InstallerDownloadUrl` to the blob URL (see `infra/README.md`).
