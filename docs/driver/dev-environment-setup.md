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

Two options. Pick one:

**Option A - Microsoft's Windows 11 Dev VM (recommended for speed).**
Microsoft publishes a pre-built `.vhdx` of Windows 11 Enterprise with
VS 2022 already installed. Free, valid 90 days (renewable by re-downloading).

- URL: <https://developer.microsoft.com/windows/downloads/virtual-machines/>
- Pick the "Hyper-V" image (~20 GB download).
- Pros: zero install time, comes with VS already.
- Cons: 90-day expiry. We do not care for now.

**Option B - Clean Windows 11 ISO.**
Download a Windows 11 Pro ISO, create a fresh VM, install. Works long
term. Adds ~30 minutes vs Option A.

### 3c. Create the VM

Once the `.vhdx` (Option A) or ISO (Option B) is on disk:

```powershell
# Run as administrator
$vmName   = 'ScamAlertDev'
$switch   = 'Default Switch'   # comes with Hyper-V on Pro
$vhdxPath = 'D:\HyperV\ScamAlertDev\ScamAlertDev.vhdx'   # your path

New-VM -Name $vmName -MemoryStartupBytes 8GB -Generation 2 `
       -VHDPath $vhdxPath -SwitchName $switch
Set-VMProcessor $vmName -Count 4
Set-VMMemory    $vmName -DynamicMemoryEnabled $false
Set-VMFirmware  $vmName -EnableSecureBoot Off    # required for test-signing
Start-VM        $vmName
```

8 GB RAM and 4 vCPUs is plenty for a driver dev box. Disable Dynamic
Memory because kernel-mode allocation behavior is more predictable
without it.

### 3d. Inside the VM, one-time setup

Connect via the Hyper-V Manager console once, then run **inside the
VM** (admin PowerShell):

```powershell
# Enable test-signing so unsigned drivers will load
bcdedit /set testsigning on

# Allow PowerShell Direct from the host
Enable-PSRemoting -Force
Set-NetFirewallRule -Name 'WINRM-HTTP-In-TCP' -Enabled True

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
