# ScamAlert desktop installer (MSI)

Packages **ScamAlert.Broker** (Windows service `ScamAlertBroker`), **ScamAlert.Tray** (runs at logon), and **ScamAlert.Configurator** (pairing wizard).

## Build

Requires Windows and the [.NET 10 SDK](https://dotnet.microsoft.com/download). WiX is restored automatically via `WixToolset.Sdk` (NuGet).

The installer project is in the solution for editing but **not** built by default with `dotnet build ScamAlert.sln` (avoids RID conflicts with Broker). Always use the script below or build the `.wixproj` directly.

```powershell
.\scripts\build-desktop-installer.ps1
# Production: bake your live API URL so families only enter the pairing code
.\scripts\build-desktop-installer.ps1 -ApiBaseUrl "https://your-app.azurewebsites.net"
```

Output: `installer\ScamAlert.DesktopInstaller\bin\Release\ScamAlert-Desktop.msi` (name may vary slightly by WiX version).

## Install

1. Run the MSI elevated (per-machine install under `Program Files\ScamAlert`).
2. The **Pair this PC** wizard opens — enter the code from **Devices → Pair PC** (and your website URL only if the installer was not built with a baked-in API address).
3. Confirm services: `sc query ScamAlertBroker` should show **RUNNING** (or start it).
4. Sign out/in (or reboot) so Tray runs in the user session. Re-open the wizard from **Start Menu → ScamAlert → Pair this PC** if needed.

## Layout

| Path | Purpose |
|------|---------|
| `Program Files\ScamAlert\Broker\` | Broker service binaries |
| `Program Files\ScamAlert\Tray\` | Tray UI binaries |
| `Program Files\ScamAlert\Setup\` | Pairing wizard (`ScamAlert.Configurator.exe`) |
| `%ProgramData%\ScamAlert\` | `broker.appsettings.json` (written by pairing script) |

## Signing (production)

Beta builds can use unsigned MSIs (SmartScreen warning). For production, sign the MSI with your code-signing certificate:

```powershell
signtool sign /fd SHA256 /a "path\to\ScamAlert-Desktop.msi"
```

## Azure blob upload (after deploy)

Upload the MSI to the storage account `installers` container and set `Web__InstallerDownloadUrl` to the blob URL (see `infra/README.md`).
