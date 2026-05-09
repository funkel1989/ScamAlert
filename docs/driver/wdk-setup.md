# WDK Setup For ScamAlert WFP Monitor

ScamAlert's real monitor is a kernel-mode Windows Filtering Platform
callout driver. Build it on the host with the WDK + Visual Studio. Run
and test it inside a Windows 11 VM with test-signing enabled.

## Required Tooling On The Host

- **Visual Studio 2022 or 2026 with the "Desktop development with C++"
  workload.** This box already has VS Community 2026 installed.
- **Windows 11 SDK 10.0.26100.0** - already installed on this box.
- **Windows Driver Kit 10.0.26100** matching the SDK version. Not yet
  installed.
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

Expected after WDK install:

- `NtddkHeader` resolves to `...\Include\10.0.26100.0\km\ntddk.h`.
- `FwpskHeader` resolves to `...\Include\10.0.26100.0\km\fwpsk.h`.
- `FwpkclntLibrary` resolves to
  `...\Lib\10.0.26100.0\km\x64\fwpkclnt.lib`.
- `VisualStudio` reports the installed VS edition.

## Validate The VM

Run **inside the VM**:

```powershell
scripts/driver/check-driver-prereqs.ps1 -CheckTestSigning
```

Expected: same as host plus `TestSigningEnabled = True`.

## Where To Get The WDK

Microsoft's official download landing page:

<https://learn.microsoft.com/windows-hardware/drivers/download-the-wdk>

Pick the WDK that matches the installed SDK (10.0.26100). The
installer is interactive and requires admin rights. Reboot after the
install completes if prompted.

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
