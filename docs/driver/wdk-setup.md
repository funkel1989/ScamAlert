# WDK Setup For ScamAlert WFP Monitor

ScamAlert's real monitor is a kernel-mode Windows Filtering Platform
callout driver. Build it on the host with the WDK + Visual Studio. Run
and test it inside a Windows 11 VM with test-signing enabled.

## Required Tooling On The Host

- **Visual Studio 2022 or 2026 with the "Desktop development with C++"
  workload.** This box already has VS Community 2026 installed.
- **Windows 11 SDK 10.0.28000.x** - already installed on this box.
  (10.0.26100.x also works.)
- **Windows Driver Kit** - install via **Visual Studio Installer ->
  Modify -> Individual components -> "Windows Driver Kit"**. This
  delivers the `Microsoft.Windows.DriverKit` VSIX (project templates +
  MSBuild targets). Kernel headers and libraries (`ntddk.h`,
  `fwpsk.h`, `fwpkclnt.lib`) are pulled from the
  **`Microsoft.Windows.WDK.x64`** NuGet package referenced from the
  driver `.vcxproj` at build time, not from a global `Windows Kits\10`
  install. The legacy `wdksetup.exe` standalone installer also works
  but is no longer required.
- **Visual Studio C++ Spectre-mitigated libraries (latest)** -
  add via Visual Studio Installer "Modify" -> "Individual components".
  WDK templates require these to link.
- **Administrator PowerShell** for the install scripts.

## Required In The VM

- Windows 11 (Microsoft's free Win11 Dev VM `.vhdx` is the recommended
  starting image).
- Test-signing enabled: `bcdedit /set testsigning on`.
- Secure Boot turned off (set on the Hyper-V VM via
  `Set-VMFirmware -EnableSecureBoot Off`).
- PowerShell remoting (`Enable-PSRemoting -Force`) so the host can
  drive the VM via `Invoke-Command -VMName`.
- Stable computer name (we use `ScamAlertDev`).

Do not enable test-signing on the host - it is a VM-only posture.

## Validate The Host

Run on the host:

```powershell
scripts/driver/check-driver-prereqs.ps1
```

Expected after WDK install (modern path):

- `VsWdkExtensionPresent` is `True`.
- `VsWdkExtensionVersion` reports a 10.0.x version
  (e.g. `10.0.26586.0`).
- `NuGetWdkPackages` may say `<none yet - will populate on first build>`
  before the first driver build, and resolve to
  `microsoft.windows.wdk.x64` etc. afterward.
- `VisualStudio` reports the installed VS edition.

Or after WDK install (legacy path - only if you also ran
`wdksetup.exe`):

- `LegacyWdkPresent` is `True` and
- `NtddkHeader`, `FwpskHeader`, `FwpkclntLibrary` resolve to real paths
  under `...\Include\10.0.x\km` and `...\Lib\10.0.x\km\x64`.

Either path is acceptable. The driver `.vcxproj` will use the NuGet
package even when the legacy path is also present.

## Validate The VM

Run **inside the VM**:

```powershell
scripts/driver/check-driver-prereqs.ps1 -CheckTestSigning
```

Expected: same as host plus `TestSigningEnabled = True`.

## Where To Get The WDK

The recommended path is via the Visual Studio Installer. Open
`Visual Studio Installer` -> Modify your VS edition ->
"Individual components" tab -> check **"Windows Driver Kit"** ->
Modify. This installs the project templates and MSBuild targets.
Kernel headers and libraries are pulled per-project at build time
from the `Microsoft.Windows.WDK.x64` NuGet package, so no separate
installer is needed.

The legacy standalone installer is still published at
<https://learn.microsoft.com/windows-hardware/drivers/download-the-wdk>
if you prefer global headers/libs under
`C:\Program Files (x86)\Windows Kits\10\Include\<ver>\km`. Match the
WDK version to the installed SDK if you take this route.

## VS Component Add-On

Open `Visual Studio Installer` -> Modify the installed VS edition ->
"Individual components" tab -> ensure these are checked:

- `MSVC v143 - VS 2022 C++ x64/x86 Spectre-mitigated libs (latest)`
- `Windows 11 SDK (10.0.26100.0)` (already installed - just verify)

The first item is the one that breaks WDK template links if missing.

## VS 2026 Compatibility Note

Visual Studio Community 2026 was released recently. The WDK Visual
Studio extension historically targets a specific VS major version. If
the WDK templates do not appear in `File > New > Project` after
installing the WDK, install the standalone `WDK.vsix` from the same
Microsoft download page and verify that "Empty WDM Driver" appears
under C++.

## Test-Signing Inside The VM

Run **inside the VM** as administrator:

```powershell
bcdedit /set testsigning on
Restart-Computer
```

After the reboot the VM desktop should show `Test Mode` watermark in
the lower-right corner. Reverting:

```powershell
bcdedit /set testsigning off
Restart-Computer
```

See [`dev-environment-setup.md`](dev-environment-setup.md) for the
full Hyper-V VM bring-up runbook.
