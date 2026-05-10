# ScamAlert WFP Driver - Dev Environment Setup

This is a one-time bring-up guide. It captures what to install on the
Windows host (where you build the driver) and how to stand up a Windows
VM (where you run/test the driver).

## Why Two Machines

A WFP callout driver is unsigned during development. Loading it on a
real workstation requires either a real Microsoft signing certificate
or putting the OS into **test-signing** mode - which is a security
posture you do **not** want on your daily-driver machine and which also
turns off Secure Boot UI for the kernel. Standard practice:

- **Host (your daily machine):** WDK + Visual Studio. You build `.sys`
  here. No test-signing.
- **VM (a dev VM):** test-signing on, Secure Boot off. You install the
  `.sys` here, generate inbound traffic to ports 22/23/3389 from
  somewhere else, and watch what the driver does.

If the driver bug-checks (BSODs) the VM, you reboot the VM, not your
work machine. Worth the friction.

## Host Snapshot (2026-05-09)

Detected on this machine:

- Windows 11 Pro 25H2, build 26200.
- AMD Ryzen 9 9950X, virtualization enabled in firmware, hypervisor
  already present (Hyper-V is running for something - probably
  WSL or an existing VM).
- Visual Studio Community 2026 (18.5.11723.231) installed at
  `C:\Program Files\Microsoft Visual Studio\18\Community`.
- Windows 10 SDK 10.0.19041.0 and Windows 11 SDK 10.0.26100.0 both
  installed.
- WDK is **not** installed: `fwpsk.h`, `fwpkclnt.lib`, and `ntddk.h`
  are not on disk.

So we need: WDK 10.0.26100 + the Spectre-mitigated MSVC libs in VS,
plus a Windows VM.

## Step 1 - Install The WDK On The Host

The WDK is split into three pieces that must all line up:

1. **WDK installer** - downloads the headers (`ntddk.h`, `fwpsk.h`,
   etc.) and libraries (`fwpkclnt.lib`) under
   `C:\Program Files (x86)\Windows Kits\10\Include\10.0.26100.0\km`
   and `...\Lib\10.0.26100.0\km\x64`.
2. **VS 2026 driver workload components** - the `Spectre-mitigated
   libraries (latest)` and `MSVC v143 - VS 2022 C++ x64/x86 build
   tools` items. WDK templates pull in the Spectre libs and refuse to
   link without them.
3. **WDK Visual Studio extension (`WDK.vsix`)** - adds the driver
   project templates and integrates the `inf2cat` / signing build
   steps.

Where to get them:

- Microsoft official: <https://learn.microsoft.com/windows-hardware/drivers/download-the-wdk>
- The WDK version must match an installed SDK version exactly. Target
  WDK 10.0.26100 since you already have SDK 10.0.26100.0.

Order of installs (do not skip):

1. Confirm Windows 11 SDK 10.0.26100.0 is present (it is - skip).
2. Run the WDK 10.0.26100 installer (admin, on the host).
3. Open Visual Studio Installer, select VS Community 2026, click
   "Modify". Under "Individual components", check:
   - `MSVC v143 - VS 2022 C++ x64/x86 Spectre-mitigated libs (latest)`
   - `Windows 11 SDK (10.0.26100.0)` (already there, just verify).
4. Reopen Visual Studio. Confirm `File > New > Project` shows the
   "Empty WDM Driver" template under C++ / Windows Driver. If it does
   not, install the `WDK.vsix` from the WDK download page manually.

Verify after install by running (on the host):

```powershell
scripts/driver/check-driver-prereqs.ps1
```

`FwpskHeader` and `FwpkclntLibrary` should both resolve. We will add
that script as part of Task 0.

## Step 2 - Choose A VM Platform

The single biggest call. Three reasonable options.

| Option | Pros | Cons |
| --- | --- | --- |
| **Hyper-V** | Built into Win 11 Pro, free, no licensing, **PowerShell Direct** lets the host run commands inside the VM with no VM networking | Needs admin install of the Hyper-V optional feature; tooling is Microsoft-only |
| **VMware Workstation Pro** | Free for personal use as of 2024, mature snapshot UI, good for kernel debugging over named pipes | Extra installer; no PowerShell Direct equivalent (use SSH/WinRM) |
| **VirtualBox** | Free, cross-platform | Slower, more fragile interaction with Windows hypervisor stack; not recommended on the same box that runs Hyper-V |

**Recommendation: Hyper-V.** Three reasons:

1. The hypervisor is already running on this machine, so we are
   already paying its perf cost.
2. **PowerShell Direct** (`Invoke-Command -VMName ...`) lets Claude
   Code drive the VM from the host with zero VM networking config.
   That is the cleanest "so you can access it" answer.
3. WinDbg's kernel debugger network transport (`kdnet`) integrates
   well with Hyper-V VMs.

## Step 3 - Stand Up The VM (Hyper-V Path)

### 3a. Enable Hyper-V if not already on

Open an **admin** PowerShell:

```powershell
Enable-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V-All -All
```

Reboot when prompted. Re-run after reboot to confirm:

```powershell
(Get-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V).State
```

Expect `Enabled`.

### 3b. Pick an OS image

Microsoft retired the pre-built `Windows 11 Dev VM .vhdx` downloads in
2025 - the old `developer.microsoft.com/windows/downloads/virtual-machines`
page no longer publishes them and there is no direct replacement.

The supported path now is the **Windows 11 Enterprise evaluation ISO**:

- URL: <https://www.microsoft.com/en-us/evalcenter/evaluate-windows-11-enterprise>
- Pick `Windows 11 Enterprise, version 25H2, x64, ISO`.
- 90-day evaluation, no product key required. Refresh by reinstalling
  when it expires (we will be done with this VM well before then).

We do **not** want the IoT Enterprise LTSC ISO - it intentionally
omits some store/dev tooling we may want later. Plain Win11 Enterprise
is the right pick.

The download lands as `Win11_25H2_EnglishInternational_x64.iso` or
similar. Park it under `D:\HyperV\ISOs\` and remember the path.

### 3b-note. Win11 install requirements vs Hyper-V

Windows 11 Setup will refuse to install on a VM that does not look
"modern enough". For Hyper-V you must hit **all** of the following or
the installer bails with a hardware-compatibility error:

- VM **Generation 2** (UEFI, not BIOS).
- vTPM enabled (`Set-VMKeyProtector` + `Enable-VMTPM`).
- 4+ GB RAM, 64+ GB disk, 2+ vCPUs.
- Secure Boot **ON** for the Windows 11 install.

Test-signing for unsigned drivers requires Secure Boot **OFF**, so the
order of operations matters: leave Secure Boot ON during install, then
flip it OFF and enable test-signing once Windows is up. The PowerShell
in `3c` does exactly that.

### 3c. Create the VM (run as administrator on the host)

```powershell
$vmName   = 'ScamAlertDev'
$switch   = 'Default Switch'                                       # comes with Hyper-V on Pro
$vmRoot   = 'D:\HyperV\ScamAlertDev'
$isoPath  = 'D:\HyperV\ISOs\Win11_25H2_EnglishInternational_x64.iso'  # update to your downloaded ISO path
$vhdxPath = Join-Path $vmRoot 'ScamAlertDev.vhdx'

New-Item -ItemType Directory -Force -Path $vmRoot | Out-Null

# Fresh dynamic VHDX, 80 GB max
New-VHD -Path $vhdxPath -Dynamic -SizeBytes 80GB

New-VM -Name $vmName -MemoryStartupBytes 8GB -Generation 2 `
       -VHDPath $vhdxPath -SwitchName $switch

Set-VMProcessor $vmName -Count 4
Set-VMMemory    $vmName -DynamicMemoryEnabled $false

# vTPM is mandatory for Win11. The HgsKeyProtector pair lets us enable it without Active Directory.
Set-VMKeyProtector -VMName $vmName -NewLocalKeyProtector
Enable-VMTPM       -VMName $vmName

# Mount the install ISO and force first boot from DVD
Add-VMDvdDrive  -VMName $vmName -Path $isoPath
$dvd = Get-VMDvdDrive -VMName $vmName
Set-VMFirmware  -VMName $vmName -FirstBootDevice $dvd

# Secure Boot must be ON during Win11 install. We turn it OFF after install.
Set-VMFirmware -VMName $vmName -EnableSecureBoot On

Start-VM $vmName
vmconnect.exe $env:COMPUTERNAME $vmName
```

8 GB RAM, 4 vCPUs, 80 GB disk is plenty for a driver dev box. Dynamic
memory is off because kernel-mode allocation behavior is more
predictable without it.

The `vmconnect.exe` line opens the Hyper-V console window so you can
click through the Windows 11 installer:

- "Windows 11 Enterprise" edition.
- "Custom: Install Windows only" -> select the single 80 GB unpartitioned drive.
- Skip Microsoft account: pick "domain join" path on the network screen
  to land on a local-account-only setup, or just disable the network
  adapter mid-OOBE (`Set-VMNetworkAdapter -VMName ScamAlertDev -DeviceNaming On -SwitchName ''`).
- Set local admin user `dev`, give it any password you will remember.

After Windows finishes setup and reaches the desktop, **shut the VM
down cleanly** (Start menu > Power > Shut down) before the next step.

### 3d. Flip Secure Boot off, eject install ISO (run on host as admin)

VM should be powered off. Then:

```powershell
$vmName = 'ScamAlertDev'

# Test-signing requires Secure Boot off
Set-VMFirmware -VMName $vmName -EnableSecureBoot Off

# Detach the install ISO so it does not boot from DVD again
Get-VMDvdDrive -VMName $vmName | Remove-VMDvdDrive

Start-VM $vmName
vmconnect.exe $env:COMPUTERNAME $vmName
```

### 3e. Inside the VM, one-time setup

Connect via the Hyper-V console (the `vmconnect.exe` above), log in,
open an **admin PowerShell** inside the VM, and run:

```powershell
# Enable test-signing so unsigned drivers will load
bcdedit /set testsigning on

# Allow PowerShell remoting (so the host can drive the VM via Invoke-Command)
Enable-PSRemoting -Force
Set-NetFirewallRule -Name 'WINRM-HTTP-In-TCP' -Enabled True

# Trust the host as a remoting client (PowerShell Direct still works without this,
# but having it makes WinRM-over-network optional in case PS Direct breaks)
Set-Item WSMan:\localhost\Client\TrustedHosts -Value '*' -Force

# Pin a stable computer name so we can target it from the host
Rename-Computer -NewName ScamAlertDev -Force

Restart-Computer
```

After the reboot you should see `Test Mode` in the lower-right corner
of the VM desktop. That is how we know test-signing is live.

## Step 4 - Confirm Host Can Drive The VM

Back on the host, in a PowerShell that knows the VM credential:

```powershell
$cred = Get-Credential   # local admin inside the VM
Invoke-Command -VMName ScamAlertDev -Credential $cred -ScriptBlock {
    [PSCustomObject]@{
        TestSigning = (bcdedit /enum '{current}' | Select-String 'testsigning\s+Yes') -ne $null
        Computer    = $env:COMPUTERNAME
    }
}
```

Expected output:

```text
TestSigning Computer
----------- --------
       True ScamAlertDev
```

That is the green light. From this point on, Claude Code can issue
`Invoke-Command -VMName ScamAlertDev` to install drivers, start
services, view event logs, and tear down between iterations - all
without leaving the host.

## Step 5 - Optional: Wire WinDbg For Kernel Debugging

Not required for observe-only validation, but very useful when the
driver inevitably crashes the VM. Do this once:

```powershell
# Inside VM, admin PowerShell
bcdedit /debug on
bcdedit /dbgsettings net hostip:<HOST-IP-ON-DEFAULT-SWITCH> port:50000 key:1.1.1.1
```

Then on the host, point WinDbg at `net:port=50000,key=1.1.1.1`.

## Iteration Loop You Will Use Daily

1. On the host: edit C++, run `scripts/driver/build-driver.ps1`.
2. Copy the freshly-built `.sys` + `.inf` into the VM (PowerShell
   Direct copies via `Copy-Item -ToSession`).
3. Inside the VM (via `Invoke-Command -VMName`): `pnputil /add-driver`,
   `sc start ScamAlertWfp`.
4. Generate inbound traffic to 22/23/3389 from the host or another VM.
5. Read driver-event JSONL on the host (broker writes it locally).
6. `sc stop` and `pnputil /delete-driver` between iterations.

We will codify steps 2-3 and 6 as scripts under
`scripts/driver/` once the project bootstraps.

## What You Have To Do Manually

The only steps that require admin elevation and that I cannot drive
for you from here are:

- The WDK installer GUI run.
- The Visual Studio Installer "Modify" run.
- Enabling Hyper-V (`Enable-WindowsOptionalFeature`).
- The first console connect to the new VM, including the inside-VM
  test-signing toggle and rename.

After that initial bring-up, everything else can be scripted from the
host.
