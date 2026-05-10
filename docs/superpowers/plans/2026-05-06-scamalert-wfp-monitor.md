# ScamAlert WFP Monitor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the driver simulator with an actual Windows Filtering Platform monitor that observes inbound RDP, SSH, and Telnet attempts and feeds them into the existing broker/tray decision loop.

**Architecture:** The first native milestone is observe-only: a C++ WFP callout driver detects inbound protected attempts and queues events to a user-mode bridge. The bridge forwards those events to the existing broker pipe and returns broker decisions to the driver bridge boundary, but the WFP driver does not pend/block traffic until observe-only behavior is proven in a VM.

**Tech Stack:** C++ WDK kernel driver, .NET 8 bridge service, Win32 DeviceIoControl, Windows Filtering Platform ALE receive/accept layers, existing ScamAlert broker/tray contracts.

---

## Scope Check

This plan intentionally builds the actual monitor in two safe increments:

- **Milestone A:** Native bridge plus observe-only WFP driver. This replaces the simulator as the event source and proves that real inbound protected attempts reach the broker/tray loop.
- **Milestone B:** Pend-and-decide enforcement with `FwpsPendOperation0` / `FwpsCompleteOperation0`. This requires a separate plan after observe-only validation because the ALE receive/accept pend path has packet clone/reinject details and higher risk.

This plan completes Milestone A and prepares the IOCTL contract so Milestone B can be added without changing broker/tray policy code.

## Current Environment Note

This machine currently has the Windows 10 SDK folders, but the WDK kernel-mode headers/libraries were not found:

- `fwpsk.h` not found under `C:\Program Files (x86)\Windows Kits\10\Include`
- `fwpkclnt.lib` not found under `C:\Program Files (x86)\Windows Kits\10\Lib`

Tasks 1-3 can run without WDK. Tasks 4-8 require the Windows Driver Kit and Visual Studio driver tooling.

## File Structure

- `src/ScamAlert.Broker.Client/` - .NET client library for the existing broker driver pipe protocol.
- `src/ScamAlert.DriverBridge/` - .NET worker service that opens the driver device, receives driver events, forwards them to the broker, and sends decisions back to the driver.
- `native/ScamAlert.Driver.Shared/ScamAlertDriverIoctl.h` - C-compatible IOCTL and binary struct contract shared by driver and bridge.
- `native/ScamAlert.WfpDriver/` - C++ WDK kernel driver project.
- `native/ScamAlert.WfpDriver/ScamAlertWfpDriver.vcxproj` - WDK project file.
- `native/ScamAlert.WfpDriver/*.cpp/*.h/*.inf` - driver entry, device/IOCTL, event queue, WFP registration, and package metadata.
- `scripts/driver/` - admin-only helper scripts for test signing, install, uninstall, and log collection.
- `tests/ScamAlert.Core.Tests/` - .NET tests for broker pipe client, bridge mapping, IOCTL struct layout, and timeout/fallback behavior.
- `docs/driver/` - WDK setup, VM validation, and observe-only runbook.

## Constants And Contract Values

Use these stable values throughout the plan:

```text
Device name: \Device\ScamAlertWfp
DOS device link: \DosDevices\ScamAlertWfp
User-mode path: \\.\ScamAlertWfp
Device type: 0x8000
IOCTL_SCAMALERT_GET_EVENT: CTL_CODE(0x8000, 0x801, METHOD_BUFFERED, FILE_READ_DATA)
IOCTL_SCAMALERT_COMPLETE_EVENT: CTL_CODE(0x8000, 0x802, METHOD_BUFFERED, FILE_WRITE_DATA)
Callout IPv4 key: 585493A7-CF45-4551-ABCF-111BA6007130
Callout IPv6 key: BA321121-D6AF-4AA9-907C-F365D7C2684A
Sublayer key: 653537B3-4364-4E17-A99C-45F31AF2B9ED
Provider key: B4EDE861-10F7-4103-8951-94D253F7AE67
```

Binary contract rules:

- All binary structs are little-endian.
- Structs use fixed-size fields only.
- IP addresses are text in `WCHAR[46]` so IPv4 and IPv6 share one contract.
- Kernel strings must be null-terminated before queueing to user mode.
- Observe-only driver accepts decisions from bridge but does not enforce them yet.

## Task 0: WDK And VM Readiness

**Files:**
- Create: `docs/driver/wdk-setup.md`
- Create: `scripts/driver/check-driver-prereqs.ps1`

- [ ] **Step 1: Add prerequisite checker**

Create `scripts/driver/check-driver-prereqs.ps1`:

```powershell
$ErrorActionPreference = 'Stop'

$kitRoot = 'C:\Program Files (x86)\Windows Kits\10'
$includeRoot = Join-Path $kitRoot 'Include'
$libRoot = Join-Path $kitRoot 'Lib'

$fwpsk = Get-ChildItem -Path $includeRoot -Recurse -Filter fwpsk.h -ErrorAction SilentlyContinue | Select-Object -First 1
$fwpkclnt = Get-ChildItem -Path $libRoot -Recurse -Filter fwpkclnt.lib -ErrorAction SilentlyContinue | Select-Object -First 1

[pscustomobject]@{
    WindowsKitsRoot = (Test-Path -LiteralPath $kitRoot)
    FwpskHeader = $fwpsk.FullName
    FwpkclntLibrary = $fwpkclnt.FullName
    TestSigning = ((bcdedit /enum '{current}' | Select-String -Pattern 'testsigning\\s+Yes') -ne $null)
} | Format-List

if (-not $fwpsk -or -not $fwpkclnt) {
    Write-Error 'Windows Driver Kit kernel-mode WFP headers/libraries are missing. Install the WDK before building ScamAlert.WfpDriver.'
}
```

- [ ] **Step 2: Add WDK setup documentation**

Create `docs/driver/wdk-setup.md`:

````markdown
# WDK Setup For ScamAlert WFP Monitor

ScamAlert's real monitor uses a kernel-mode Windows Filtering Platform callout driver. Build and test this only inside a Windows VM until observe-only validation passes.

## Required Tooling

- Visual Studio 2022 with C++ desktop workload.
- Windows Driver Kit matching the installed Windows SDK.
- Administrator PowerShell for driver install/uninstall scripts.
- Test-signing enabled inside the test VM.

## Validation

Run:

```powershell
scripts/driver/check-driver-prereqs.ps1
```

Expected result:

- `FwpskHeader` points to `fwpsk.h`.
- `FwpkclntLibrary` points to `fwpkclnt.lib`.
- `TestSigning` is `True` in the VM before installing the test driver.

## Test-Signing

Inside the VM only:

```powershell
bcdedit /set testsigning on
Restart-Computer
```

Do not enable test-signing on a primary workstation for normal use.
````

- [ ] **Step 3: Run prerequisite check**

Run:

```powershell
scripts/driver/check-driver-prereqs.ps1
```

Expected on a machine without WDK: FAIL with `Windows Driver Kit kernel-mode WFP headers/libraries are missing`.

Expected on a driver VM: PASS and print WDK paths.

- [ ] **Step 4: Commit readiness docs**

Run:

```powershell
git add docs/driver/wdk-setup.md scripts/driver/check-driver-prereqs.ps1
git commit -m "docs: add WDK monitor prerequisites"
```

Expected: commit succeeds. Do not stage `ScamAlert.slnLaunch`.

## Task 1: Shared Broker Pipe Client

**Files:**
- Create: `src/ScamAlert.Broker.Client/ScamAlert.Broker.Client.csproj`
- Create: `src/ScamAlert.Broker.Client/BrokerDriverPipeClient.cs`
- Modify: `tools/ScamAlert.DriverSimulator/ScamAlert.DriverSimulator.csproj`
- Modify: `tools/ScamAlert.DriverSimulator/Program.cs`
- Modify: `tests/ScamAlert.Core.Tests/ScamAlert.Core.Tests.csproj`
- Create: `tests/ScamAlert.Core.Tests/Broker/BrokerDriverPipeClientTests.cs`
- Modify: `ScamAlert.sln`

- [ ] **Step 1: Create failing broker pipe client tests**

Create `tests/ScamAlert.Core.Tests/Broker/BrokerDriverPipeClientTests.cs`:

```csharp
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using ScamAlert.Broker.Client;
using ScamAlert.Contracts;

namespace ScamAlert.Core.Tests.Broker;

public sealed class BrokerDriverPipeClientTests
{
    [Fact]
    public async Task SendAttemptAsyncReturnsDecisionForMatchingResponse()
    {
        var pipeName = $"scamalert-test-{Guid.NewGuid():N}";
        var attempt = CreateAttempt();
        var server = RunServerAsync(pipeName, attempt.EventId, DriverDecisionKind.Allow);
        var client = new BrokerDriverPipeClient(pipeName, TimeSpan.FromSeconds(2));

        var decision = await client.SendAttemptAsync(attempt, CancellationToken.None);

        Assert.NotNull(decision);
        Assert.Equal(DriverDecisionKind.Allow, decision.Decision);
        await server;
    }

    [Fact]
    public async Task SendAttemptAsyncReturnsNullForMismatchedResponse()
    {
        var pipeName = $"scamalert-test-{Guid.NewGuid():N}";
        var attempt = CreateAttempt();
        var server = RunServerAsync(pipeName, Guid.NewGuid(), DriverDecisionKind.Allow);
        var client = new BrokerDriverPipeClient(pipeName, TimeSpan.FromSeconds(2));

        var decision = await client.SendAttemptAsync(attempt, CancellationToken.None);

        Assert.Null(decision);
        await server;
    }

    private static async Task RunServerAsync(string pipeName, Guid responseId, DriverDecisionKind decision)
    {
        await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await pipe.WaitForConnectionAsync();
        using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

        var requestLine = await reader.ReadLineAsync();
        Assert.Contains("\"sourceIp\":\"203.0.113.44\"", requestLine);

        var response = new DriverDecisionResponse(responseId, decision, "test");
        await writer.WriteLineAsync(JsonSerializer.Serialize(response, SignalJson.Options));
    }

    private static ProtectedConnectionAttempt CreateAttempt()
    {
        return new ProtectedConnectionAttempt(
            Guid.Parse("2a212e4c-4fb4-4514-a9a2-5419894749f7"),
            DateTimeOffset.Parse("2026-05-06T12:00:00Z"),
            "203.0.113.44",
            3389,
            ProtectedService.Rdp);
    }
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```powershell
dotnet test tests/ScamAlert.Core.Tests/ScamAlert.Core.Tests.csproj --filter BrokerDriverPipeClientTests
```

Expected: build fails because `ScamAlert.Broker.Client` does not exist.

- [ ] **Step 3: Add broker client project**

Run:

```powershell
dotnet new classlib -n ScamAlert.Broker.Client -o src/ScamAlert.Broker.Client
dotnet sln ScamAlert.sln add src/ScamAlert.Broker.Client/ScamAlert.Broker.Client.csproj
dotnet add src/ScamAlert.Broker.Client/ScamAlert.Broker.Client.csproj reference src/ScamAlert.Contracts/ScamAlert.Contracts.csproj
dotnet add tools/ScamAlert.DriverSimulator/ScamAlert.DriverSimulator.csproj reference src/ScamAlert.Broker.Client/ScamAlert.Broker.Client.csproj
dotnet add tests/ScamAlert.Core.Tests/ScamAlert.Core.Tests.csproj reference src/ScamAlert.Broker.Client/ScamAlert.Broker.Client.csproj
Remove-Item -LiteralPath src/ScamAlert.Broker.Client/Class1.cs
```

- [ ] **Step 4: Implement broker pipe client**

Create `src/ScamAlert.Broker.Client/BrokerDriverPipeClient.cs`:

```csharp
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using ScamAlert.Contracts;

namespace ScamAlert.Broker.Client;

public sealed class BrokerDriverPipeClient(
    string pipeName = "scamalert-driver-events",
    TimeSpan? protocolTimeout = null)
{
    private const int MaxFrameBytes = 16 * 1024;
    private static readonly UTF8Encoding Utf8NoBomStrict = new(false, true);

    public async Task<DriverDecisionResponse?> SendAttemptAsync(
        ProtectedConnectionAttempt attempt,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(protocolTimeout ?? TimeSpan.FromSeconds(30));

        try
        {
            await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(3000, timeout.Token);

            using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true)
            {
                AutoFlush = true
            };

            await writer.WriteLineAsync(JsonSerializer.Serialize(attempt, SignalJson.Options).AsMemory(), timeout.Token);

            var responseLine = await ReadFrameAsync(pipe, timeout.Token);
            if (string.IsNullOrWhiteSpace(responseLine))
            {
                return null;
            }

            var response = JsonSerializer.Deserialize<DriverDecisionResponse>(responseLine, SignalJson.Options);
            if (response is null || response.ObservedEventId != attempt.EventId || !IsKnownDecision(response.Decision))
            {
                return null;
            }

            return response;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
        catch (BrokerPipeProtocolException)
        {
            return null;
        }
    }

    private static async Task<string> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var frame = new MemoryStream(MaxFrameBytes);
        var buffer = new byte[1];

        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken);
            if (read == 0)
            {
                return frame.Length == 0
                    ? string.Empty
                    : throw new BrokerPipeProtocolException("Response ended before LF.");
            }

            if (buffer[0] == '\n')
            {
                break;
            }

            if (frame.Length >= MaxFrameBytes)
            {
                throw new BrokerPipeProtocolException("Response exceeded frame limit.");
            }

            frame.WriteByte(buffer[0]);
        }

        var bytes = frame.ToArray();
        if (bytes.Length > 0 && bytes[^1] == '\r')
        {
            Array.Resize(ref bytes, bytes.Length - 1);
        }

        return Utf8NoBomStrict.GetString(bytes);
    }

    private static bool IsKnownDecision(DriverDecisionKind decision)
    {
        return decision is DriverDecisionKind.Allow or DriverDecisionKind.Block;
    }

    private sealed class BrokerPipeProtocolException(string message) : Exception(message);
}
```

- [ ] **Step 5: Refactor simulator to use broker client**

Replace the pipe-specific body in `tools/ScamAlert.DriverSimulator/Program.cs` with:

```csharp
using System.Text.Json;
using ScamAlert.Broker.Client;
using ScamAlert.Contracts;

const int UnprotectedPortExitCode = 2;
const int PipeProtocolFailureExitCode = 3;

var sourceIp = GetArgument("--ip", "203.0.113.10");
var port = int.Parse(GetArgument("--port", "3389"));

if (!ProtectedServiceMap.TryFromPort(port, out var service))
{
    Console.Error.WriteLine($"Port {port} is not protected by ScamAlert.");
    return UnprotectedPortExitCode;
}

var attempt = new ProtectedConnectionAttempt(Guid.NewGuid(), DateTimeOffset.UtcNow, sourceIp, port, service);
var client = new BrokerDriverPipeClient();
var response = await client.SendAttemptAsync(attempt, CancellationToken.None);
if (response is null)
{
    Console.Error.WriteLine("Broker pipe is unavailable or returned an invalid response.");
    return PipeProtocolFailureExitCode;
}

Console.WriteLine(JsonSerializer.Serialize(response, SignalJson.Options));
return 0;

string GetArgument(string name, string fallback)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
        {
            return args[i + 1];
        }
    }

    return fallback;
}
```

- [ ] **Step 6: Verify and commit broker client**

Run:

```powershell
dotnet test tests/ScamAlert.Core.Tests/ScamAlert.Core.Tests.csproj --filter BrokerDriverPipeClientTests
dotnet build ScamAlert.sln
git add src/ScamAlert.Broker.Client tools/ScamAlert.DriverSimulator tests/ScamAlert.Core.Tests ScamAlert.sln
git commit -m "feat: add broker driver pipe client"
```

Expected: tests and build pass; commit succeeds.

## Task 2: Driver Bridge Service Shell

**Files:**
- Create: `src/ScamAlert.DriverBridge/ScamAlert.DriverBridge.csproj`
- Create: `src/ScamAlert.DriverBridge/Program.cs`
- Create: `src/ScamAlert.DriverBridge/BridgeWorker.cs`
- Create: `src/ScamAlert.DriverBridge/Driver/IDriverEventSource.cs`
- Create: `src/ScamAlert.DriverBridge/Driver/SimulatedDriverEventSource.cs`
- Create: `tests/ScamAlert.Core.Tests/Bridge/BridgeWorkerMappingTests.cs`
- Modify: `ScamAlert.sln`

- [ ] **Step 1: Add bridge behavior tests**

Create `tests/ScamAlert.Core.Tests/Bridge/BridgeWorkerMappingTests.cs`:

```csharp
using ScamAlert.Contracts;
using ScamAlert.DriverBridge.Driver;

namespace ScamAlert.Core.Tests.Bridge;

public sealed class BridgeWorkerMappingTests
{
    [Fact]
    public void SimulatedSourceCreatesProtectedRdpAttempt()
    {
        var attempt = SimulatedDriverEventSource.CreateAttempt("203.0.113.55", 3389);

        Assert.Equal("203.0.113.55", attempt.SourceIp);
        Assert.Equal(3389, attempt.DestinationPort);
        Assert.Equal(ProtectedService.Rdp, attempt.ProtectedService);
    }

    [Fact]
    public void SimulatedSourceRejectsUnprotectedPort()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SimulatedDriverEventSource.CreateAttempt("203.0.113.55", 443));
    }
}
```

- [ ] **Step 2: Create bridge project**

Run:

```powershell
dotnet new worker -n ScamAlert.DriverBridge -o src/ScamAlert.DriverBridge
dotnet sln ScamAlert.sln add src/ScamAlert.DriverBridge/ScamAlert.DriverBridge.csproj
dotnet add src/ScamAlert.DriverBridge/ScamAlert.DriverBridge.csproj reference src/ScamAlert.Contracts/ScamAlert.Contracts.csproj
dotnet add src/ScamAlert.DriverBridge/ScamAlert.DriverBridge.csproj reference src/ScamAlert.Broker.Client/ScamAlert.Broker.Client.csproj
dotnet add tests/ScamAlert.Core.Tests/ScamAlert.Core.Tests.csproj reference src/ScamAlert.DriverBridge/ScamAlert.DriverBridge.csproj
Remove-Item -LiteralPath src/ScamAlert.DriverBridge/Worker.cs
```

- [ ] **Step 3: Implement event source contract**

Create `src/ScamAlert.DriverBridge/Driver/IDriverEventSource.cs`:

```csharp
using ScamAlert.Contracts;

namespace ScamAlert.DriverBridge.Driver;

public interface IDriverEventSource
{
    IAsyncEnumerable<ProtectedConnectionAttempt> ReadEventsAsync(CancellationToken cancellationToken);

    Task CompleteEventAsync(DriverDecisionResponse decision, CancellationToken cancellationToken);
}
```

Create `src/ScamAlert.DriverBridge/Driver/SimulatedDriverEventSource.cs`:

```csharp
using System.Threading.Channels;
using ScamAlert.Contracts;

namespace ScamAlert.DriverBridge.Driver;

public sealed class SimulatedDriverEventSource : IDriverEventSource
{
    private readonly Channel<ProtectedConnectionAttempt> channel = Channel.CreateUnbounded<ProtectedConnectionAttempt>();

    public SimulatedDriverEventSource()
    {
        channel.Writer.TryWrite(CreateAttempt("203.0.113.10", 3389));
    }

    public static ProtectedConnectionAttempt CreateAttempt(string sourceIp, int destinationPort)
    {
        if (!ProtectedServiceMap.TryFromPort(destinationPort, out var service))
        {
            throw new ArgumentOutOfRangeException(nameof(destinationPort), destinationPort, "Port is not protected.");
        }

        return new ProtectedConnectionAttempt(Guid.NewGuid(), DateTimeOffset.UtcNow, sourceIp, destinationPort, service);
    }

    public IAsyncEnumerable<ProtectedConnectionAttempt> ReadEventsAsync(CancellationToken cancellationToken)
    {
        return channel.Reader.ReadAllAsync(cancellationToken);
    }

    public Task CompleteEventAsync(DriverDecisionResponse decision, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 4: Implement bridge worker**

Create `src/ScamAlert.DriverBridge/BridgeWorker.cs`:

```csharp
using ScamAlert.Broker.Client;
using ScamAlert.DriverBridge.Driver;

namespace ScamAlert.DriverBridge;

public sealed class BridgeWorker(
    IDriverEventSource driverEvents,
    BrokerDriverPipeClient brokerClient,
    ILogger<BridgeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var attempt in driverEvents.ReadEventsAsync(stoppingToken))
        {
            logger.LogInformation(
                "Forwarding driver event {EventId} from {SourceIp}:{DestinationPort}",
                attempt.EventId,
                attempt.SourceIp,
                attempt.DestinationPort);

            var decision = await brokerClient.SendAttemptAsync(attempt, stoppingToken);
            if (decision is null)
            {
                logger.LogWarning("Broker did not return a usable decision for event {EventId}.", attempt.EventId);
                continue;
            }

            await driverEvents.CompleteEventAsync(decision, stoppingToken);
        }
    }
}
```

Replace `src/ScamAlert.DriverBridge/Program.cs`:

```csharp
using ScamAlert.Broker.Client;
using ScamAlert.DriverBridge;
using ScamAlert.DriverBridge.Driver;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton(new BrokerDriverPipeClient());
builder.Services.AddSingleton<IDriverEventSource, SimulatedDriverEventSource>();
builder.Services.AddHostedService<BridgeWorker>();

var host = builder.Build();
await host.RunAsync();
```

- [ ] **Step 5: Verify and commit bridge shell**

Run:

```powershell
dotnet test tests/ScamAlert.Core.Tests/ScamAlert.Core.Tests.csproj --filter BridgeWorkerMappingTests
dotnet build ScamAlert.sln
git add src/ScamAlert.DriverBridge tests/ScamAlert.Core.Tests ScamAlert.sln
git commit -m "feat: add driver bridge service shell"
```

Expected: tests and build pass; commit succeeds.

## Task 3: Binary IOCTL Contract

**Files:**
- Create: `native/ScamAlert.Driver.Shared/ScamAlertDriverIoctl.h`
- Create: `src/ScamAlert.DriverBridge/Driver/NativeDriverContracts.cs`
- Create: `tests/ScamAlert.Core.Tests/Bridge/NativeDriverContractsTests.cs`

- [ ] **Step 1: Add C-compatible IOCTL header**

Create `native/ScamAlert.Driver.Shared/ScamAlertDriverIoctl.h`:

```cpp
#pragma once

#include <stdint.h>

#define SCAMALERT_DEVICE_TYPE 0x8000
#define IOCTL_SCAMALERT_GET_EVENT CTL_CODE(SCAMALERT_DEVICE_TYPE, 0x801, METHOD_BUFFERED, FILE_READ_DATA)
#define IOCTL_SCAMALERT_COMPLETE_EVENT CTL_CODE(SCAMALERT_DEVICE_TYPE, 0x802, METHOD_BUFFERED, FILE_WRITE_DATA)

#define SCAMALERT_MAX_IP_CHARS 46

typedef enum SCAMALERT_PROTECTED_SERVICE : uint32_t
{
    ScamAlertServiceRdp = 1,
    ScamAlertServiceSsh = 2,
    ScamAlertServiceTelnet = 3
} SCAMALERT_PROTECTED_SERVICE;

typedef enum SCAMALERT_DRIVER_DECISION : uint32_t
{
    ScamAlertDecisionAllow = 1,
    ScamAlertDecisionBlock = 2
} SCAMALERT_DRIVER_DECISION;

#pragma pack(push, 1)
typedef struct SCAMALERT_CONNECTION_EVENT
{
    uint8_t EventId[16];
    uint64_t OccurredAtUnixTimeMilliseconds;
    wchar_t SourceIp[SCAMALERT_MAX_IP_CHARS];
    uint16_t DestinationPort;
    uint32_t ProtectedService;
} SCAMALERT_CONNECTION_EVENT;

typedef struct SCAMALERT_CONNECTION_DECISION
{
    uint8_t EventId[16];
    uint32_t Decision;
} SCAMALERT_CONNECTION_DECISION;
#pragma pack(pop)
```

- [ ] **Step 2: Add C# matching structs**

Create `src/ScamAlert.DriverBridge/Driver/NativeDriverContracts.cs`:

```csharp
using System.Runtime.InteropServices;

namespace ScamAlert.DriverBridge.Driver;

public enum NativeProtectedService : uint
{
    Rdp = 1,
    Ssh = 2,
    Telnet = 3
}

public enum NativeDriverDecision : uint
{
    Allow = 1,
    Block = 2
}

[StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Unicode)]
public struct NativeConnectionEvent
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
    public byte[] EventId;

    public ulong OccurredAtUnixTimeMilliseconds;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 46)]
    public string SourceIp;

    public ushort DestinationPort;

    public NativeProtectedService ProtectedService;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct NativeConnectionDecision
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
    public byte[] EventId;

    public NativeDriverDecision Decision;
}
```

- [ ] **Step 3: Add struct layout tests**

Create `tests/ScamAlert.Core.Tests/Bridge/NativeDriverContractsTests.cs`:

```csharp
using System.Runtime.InteropServices;
using ScamAlert.DriverBridge.Driver;

namespace ScamAlert.Core.Tests.Bridge;

public sealed class NativeDriverContractsTests
{
    [Fact]
    public void NativeStructSizesAreStable()
    {
        Assert.Equal(120, Marshal.SizeOf<NativeConnectionEvent>());
        Assert.Equal(20, Marshal.SizeOf<NativeConnectionDecision>());
    }

    [Fact]
    public void NativeEventUsesExpectedEnumValues()
    {
        Assert.Equal(1u, (uint)NativeProtectedService.Rdp);
        Assert.Equal(2u, (uint)NativeProtectedService.Ssh);
        Assert.Equal(3u, (uint)NativeProtectedService.Telnet);
    }
}
```

- [ ] **Step 4: Verify and commit IOCTL contract**

Run:

```powershell
dotnet test tests/ScamAlert.Core.Tests/ScamAlert.Core.Tests.csproj --filter NativeDriverContractsTests
dotnet build ScamAlert.sln
git add native/ScamAlert.Driver.Shared src/ScamAlert.DriverBridge tests/ScamAlert.Core.Tests
git commit -m "feat: add driver IOCTL contract"
```

Expected: tests and build pass; commit succeeds.

## Task 4: WDK Driver Skeleton

**Files:**
- Create: `native/ScamAlert.WfpDriver/ScamAlert.WfpDriver.vcxproj`
- Create: `native/ScamAlert.WfpDriver/Driver.cpp`
- Create: `native/ScamAlert.WfpDriver/Device.cpp`
- Create: `native/ScamAlert.WfpDriver/Device.h`
- Create: `native/ScamAlert.WfpDriver/Trace.h`
- Create: `native/ScamAlert.WfpDriver/ScamAlert.WfpDriver.inf`
- Create: `scripts/driver/build-driver.ps1`

- [ ] **Step 1: Verify WDK before creating driver project**

Run:

```powershell
scripts/driver/check-driver-prereqs.ps1
```

Expected: PASS. If it fails, install WDK before continuing.

- [ ] **Step 2: Add driver build script**

Create `scripts/driver/build-driver.ps1`:

```powershell
$ErrorActionPreference = 'Stop'

msbuild native\ScamAlert.WfpDriver\ScamAlert.WfpDriver.vcxproj /p:Configuration=Debug /p:Platform=x64
```

- [ ] **Step 3: Create driver skeleton source**

Create `native/ScamAlert.WfpDriver/Device.h`:

```cpp
#pragma once

#include <ntddk.h>

extern "C" DRIVER_INITIALIZE DriverEntry;

NTSTATUS ScamAlertCreateDevice(_In_ PDRIVER_OBJECT DriverObject);
VOID ScamAlertDeleteDevice();
```

Create `native/ScamAlert.WfpDriver/Driver.cpp`:

```cpp
#include "Device.h"

static VOID ScamAlertUnload(_In_ PDRIVER_OBJECT DriverObject)
{
    UNREFERENCED_PARAMETER(DriverObject);
    ScamAlertDeleteDevice();
}

extern "C"
NTSTATUS DriverEntry(
    _In_ PDRIVER_OBJECT DriverObject,
    _In_ PUNICODE_STRING RegistryPath)
{
    UNREFERENCED_PARAMETER(RegistryPath);
    DriverObject->DriverUnload = ScamAlertUnload;
    return ScamAlertCreateDevice(DriverObject);
}
```

Create `native/ScamAlert.WfpDriver/Device.cpp`:

```cpp
#include "Device.h"

#include <wdmsec.h>

static PDEVICE_OBJECT g_DeviceObject = nullptr;
static UNICODE_STRING g_DeviceName = RTL_CONSTANT_STRING(L"\\Device\\ScamAlertWfp");
static UNICODE_STRING g_SymbolicLink = RTL_CONSTANT_STRING(L"\\DosDevices\\ScamAlertWfp");
static UNICODE_STRING g_DeviceSddl = RTL_CONSTANT_STRING(L"D:P(A;;GA;;;SY)(A;;GA;;;BA)");
static const GUID g_DeviceClassGuid =
    { 0x3f01cb56, 0xa822, 0x44d7, { 0x94, 0x7e, 0x5e, 0x12, 0x76, 0xbb, 0x40, 0xd9 } };

static NTSTATUS ScamAlertCreateClose(
    _In_ PDEVICE_OBJECT DeviceObject,
    _Inout_ PIRP Irp)
{
    UNREFERENCED_PARAMETER(DeviceObject);
    Irp->IoStatus.Status = STATUS_SUCCESS;
    Irp->IoStatus.Information = 0;
    IoCompleteRequest(Irp, IO_NO_INCREMENT);
    return STATUS_SUCCESS;
}

NTSTATUS ScamAlertCreateDevice(_In_ PDRIVER_OBJECT DriverObject)
{
    NTSTATUS status = IoCreateDeviceSecure(
        DriverObject,
        0,
        &g_DeviceName,
        FILE_DEVICE_UNKNOWN,
        FILE_DEVICE_SECURE_OPEN,
        FALSE,
        &g_DeviceSddl,
        &g_DeviceClassGuid,
        &g_DeviceObject);

    if (!NT_SUCCESS(status))
    {
        return status;
    }

    DriverObject->MajorFunction[IRP_MJ_CREATE] = ScamAlertCreateClose;
    DriverObject->MajorFunction[IRP_MJ_CLOSE] = ScamAlertCreateClose;

    status = IoCreateSymbolicLink(&g_SymbolicLink, &g_DeviceName);
    if (!NT_SUCCESS(status))
    {
        IoDeleteDevice(g_DeviceObject);
        g_DeviceObject = nullptr;
        return status;
    }

    return STATUS_SUCCESS;
}

VOID ScamAlertDeleteDevice()
{
    IoDeleteSymbolicLink(&g_SymbolicLink);
    if (g_DeviceObject != nullptr)
    {
        IoDeleteDevice(g_DeviceObject);
        g_DeviceObject = nullptr;
    }
}
```

Create `native/ScamAlert.WfpDriver/Trace.h`:

```cpp
#pragma once

#define WPP_CONTROL_GUIDS
```

- [ ] **Step 4: Create WDK project and INF**

Use Visual Studio WDK Empty WDM Driver template to create `native/ScamAlert.WfpDriver/ScamAlert.WfpDriver.vcxproj`, then ensure it includes:

```xml
<TargetVersion>Windows10</TargetVersion>
<DriverType>WDM</DriverType>
<ConfigurationType>Driver</ConfigurationType>
```

Create `native/ScamAlert.WfpDriver/ScamAlert.WfpDriver.inf`:

```ini
[Version]
Signature="$WINDOWS NT$"
Class=Sample
ClassGuid={78A1C341-4539-11d3-B88D-00C04FAD5171}
Provider=%ManufacturerName%
DriverVer=05/06/2026,0.1.0.0
CatalogFile=ScamAlert.WfpDriver.cat

[DestinationDirs]
DefaultDestDir = 12

[Manufacturer]
%ManufacturerName%=Standard,NTamd64

[Standard.NTamd64]
%ScamAlert.DeviceDesc%=ScamAlert_Device, Root\ScamAlertWfp

[ScamAlert_Device.NT]
CopyFiles=Drivers_Dir

[Drivers_Dir]
ScamAlert.WfpDriver.sys

[ScamAlert_Device.NT.Services]
AddService=ScamAlertWfp,0x00000002,ScamAlert_Service

[ScamAlert_Service]
DisplayName=%ScamAlert.ServiceDesc%
ServiceType=1
StartType=3
ErrorControl=1
ServiceBinary=%12%\ScamAlert.WfpDriver.sys

[Strings]
ManufacturerName="ScamAlert"
ScamAlert.DeviceDesc="ScamAlert WFP Monitor"
ScamAlert.ServiceDesc="ScamAlert WFP Monitor Driver"
```

- [ ] **Step 5: Build skeleton driver**

Run in Developer PowerShell for Visual Studio:

```powershell
scripts/driver/build-driver.ps1
```

Expected: `.sys` builds for Debug x64.

- [ ] **Step 6: Commit skeleton**

Run:

```powershell
git add native/ScamAlert.WfpDriver scripts/driver/build-driver.ps1
git commit -m "feat: add WFP driver skeleton"
```

Expected: commit succeeds.

## Task 5: DriverBridge Device IOCTL Client

**Files:**
- Create: `src/ScamAlert.DriverBridge/Driver/NativeDriverEventSource.cs`
- Create: `src/ScamAlert.DriverBridge/Driver/NativeMethods.cs`
- Modify: `src/ScamAlert.DriverBridge/Program.cs`
- Create: `tests/ScamAlert.Core.Tests/Bridge/NativeDriverEventMappingTests.cs`

- [ ] **Step 1: Add native event mapping tests**

Create `tests/ScamAlert.Core.Tests/Bridge/NativeDriverEventMappingTests.cs`:

```csharp
using ScamAlert.Contracts;
using ScamAlert.DriverBridge.Driver;

namespace ScamAlert.Core.Tests.Bridge;

public sealed class NativeDriverEventMappingTests
{
    [Fact]
    public void ToAttemptMapsNativeRdpEvent()
    {
        var eventId = Guid.Parse("d005e9a8-7106-4230-97d8-49e383fbbe13");
        var nativeEvent = NativeDriverEventSource.CreateNativeEventForTest(
            eventId,
            "203.0.113.77",
            3389,
            NativeProtectedService.Rdp);

        var attempt = NativeDriverEventSource.ToAttemptForTest(nativeEvent);

        Assert.Equal(eventId, attempt.EventId);
        Assert.Equal("203.0.113.77", attempt.SourceIp);
        Assert.Equal(3389, attempt.DestinationPort);
        Assert.Equal(ProtectedService.Rdp, attempt.ProtectedService);
    }
}
```

- [ ] **Step 2: Implement native method declarations**

Create `src/ScamAlert.DriverBridge/Driver/NativeMethods.cs`:

```csharp
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ScamAlert.DriverBridge.Driver;

internal static partial class NativeMethods
{
    internal const uint GenericRead = 0x80000000;
    internal const uint GenericWrite = 0x40000000;
    internal const uint OpenExisting = 3;
    internal const uint FileAttributeNormal = 0x80;
    internal const uint DeviceType = 0x8000;
    internal const uint MethodBuffered = 0;
    internal const uint FileReadData = 0x0001;
    internal const uint FileWriteData = 0x0002;

    internal static readonly uint IoctlGetEvent = CtlCode(DeviceType, 0x801, MethodBuffered, FileReadData);
    internal static readonly uint IoctlCompleteEvent = CtlCode(DeviceType, 0x802, MethodBuffered, FileWriteData);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeviceIoControl(
        SafeFileHandle device,
        uint ioControlCode,
        IntPtr inBuffer,
        uint inBufferSize,
        IntPtr outBuffer,
        uint outBufferSize,
        out uint bytesReturned,
        IntPtr overlapped);

    private static uint CtlCode(uint deviceType, uint function, uint method, uint access)
    {
        return (deviceType << 16) | (access << 14) | (function << 2) | method;
    }
}
```

- [ ] **Step 3: Implement native driver event source**

Create `src/ScamAlert.DriverBridge/Driver/NativeDriverEventSource.cs`:

```csharp
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using ScamAlert.Contracts;

namespace ScamAlert.DriverBridge.Driver;

public sealed class NativeDriverEventSource : IDriverEventSource, IDisposable
{
    private const string DevicePath = @"\\.\ScamAlertWfp";
    private readonly SafeFileHandle device;

    public NativeDriverEventSource()
    {
        device = NativeMethods.CreateFileW(
            DevicePath,
            NativeMethods.GenericRead | NativeMethods.GenericWrite,
            shareMode: 0,
            securityAttributes: IntPtr.Zero,
            creationDisposition: NativeMethods.OpenExisting,
            flagsAndAttributes: NativeMethods.FileAttributeNormal,
            templateFile: IntPtr.Zero);

        if (device.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not open {DevicePath}.");
        }
    }

    public async IAsyncEnumerable<ProtectedConnectionAttempt> ReadEventsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            yield return await Task.Run(ReadOneEvent, cancellationToken);
        }
    }

    public Task CompleteEventAsync(DriverDecisionResponse decision, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var nativeDecision = new NativeConnectionDecision
            {
                EventId = decision.ObservedEventId.ToByteArray(),
                Decision = decision.Decision == DriverDecisionKind.Allow
                    ? NativeDriverDecision.Allow
                    : NativeDriverDecision.Block
            };

            var size = Marshal.SizeOf<NativeConnectionDecision>();
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(nativeDecision, buffer, false);
                if (!NativeMethods.DeviceIoControl(device, NativeMethods.IoctlCompleteEvent, buffer, (uint)size, IntPtr.Zero, 0, out _, IntPtr.Zero))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "IOCTL_SCAMALERT_COMPLETE_EVENT failed.");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }, cancellationToken);
    }

    public void Dispose()
    {
        device.Dispose();
    }

    private ProtectedConnectionAttempt ReadOneEvent()
    {
        var size = Marshal.SizeOf<NativeConnectionEvent>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!NativeMethods.DeviceIoControl(device, NativeMethods.IoctlGetEvent, IntPtr.Zero, 0, buffer, (uint)size, out _, IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "IOCTL_SCAMALERT_GET_EVENT failed.");
            }

            var nativeEvent = Marshal.PtrToStructure<NativeConnectionEvent>(buffer);
            return ToAttemptForTest(nativeEvent);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public static NativeConnectionEvent CreateNativeEventForTest(
        Guid eventId,
        string sourceIp,
        ushort destinationPort,
        NativeProtectedService protectedService)
    {
        return new NativeConnectionEvent
        {
            EventId = eventId.ToByteArray(),
            OccurredAtUnixTimeMilliseconds = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SourceIp = sourceIp,
            DestinationPort = destinationPort,
            ProtectedService = protectedService
        };
    }

    public static ProtectedConnectionAttempt ToAttemptForTest(NativeConnectionEvent nativeEvent)
    {
        var service = nativeEvent.ProtectedService switch
        {
            NativeProtectedService.Rdp => ProtectedService.Rdp,
            NativeProtectedService.Ssh => ProtectedService.Ssh,
            NativeProtectedService.Telnet => ProtectedService.Telnet,
            _ => throw new ArgumentOutOfRangeException(nameof(nativeEvent), nativeEvent.ProtectedService, "Unknown native protected service.")
        };

        return new ProtectedConnectionAttempt(
            new Guid(nativeEvent.EventId),
            DateTimeOffset.FromUnixTimeMilliseconds((long)nativeEvent.OccurredAtUnixTimeMilliseconds),
            nativeEvent.SourceIp,
            nativeEvent.DestinationPort,
            service);
    }
}
```

- [ ] **Step 4: Wire bridge source selection**

Modify `src/ScamAlert.DriverBridge/Program.cs`:

```csharp
using ScamAlert.Broker.Client;
using ScamAlert.DriverBridge;
using ScamAlert.DriverBridge.Driver;

var builder = Host.CreateApplicationBuilder(args);
var useSimulatedDriver = builder.Configuration.GetValue("DriverBridge:UseSimulatedDriver", false);

builder.Services.AddSingleton(new BrokerDriverPipeClient());
builder.Services.AddSingleton<IDriverEventSource>(_ =>
    useSimulatedDriver
        ? new SimulatedDriverEventSource()
        : new NativeDriverEventSource());
builder.Services.AddHostedService<BridgeWorker>();

var host = builder.Build();
await host.RunAsync();
```

- [ ] **Step 5: Verify and commit native bridge client**

Run:

```powershell
dotnet test tests/ScamAlert.Core.Tests/ScamAlert.Core.Tests.csproj --filter NativeDriverEventMappingTests
dotnet build ScamAlert.sln
git add src/ScamAlert.DriverBridge tests/ScamAlert.Core.Tests
git commit -m "feat: add native driver bridge client"
```

Expected: tests and build pass; commit succeeds.

## Task 6: Driver IOCTL Queue

**Files:**
- Create: `native/ScamAlert.WfpDriver/EventQueue.h`
- Create: `native/ScamAlert.WfpDriver/EventQueue.cpp`
- Modify: `native/ScamAlert.WfpDriver/Device.cpp`

- [ ] **Step 1: Add event queue header**

Create `native/ScamAlert.WfpDriver/EventQueue.h`:

```cpp
#pragma once

#include <ntddk.h>
#include "..\ScamAlert.Driver.Shared\ScamAlertDriverIoctl.h"

typedef struct SCAMALERT_EVENT_NODE
{
    LIST_ENTRY Link;
    SCAMALERT_CONNECTION_EVENT Event;
} SCAMALERT_EVENT_NODE;

NTSTATUS ScamAlertInitializeEventQueue();
VOID ScamAlertDestroyEventQueue();
NTSTATUS ScamAlertQueueConnectionEvent(_In_ const SCAMALERT_CONNECTION_EVENT* Event);
NTSTATUS ScamAlertPopConnectionEvent(_Out_ SCAMALERT_CONNECTION_EVENT* Event);
NTSTATUS ScamAlertCompleteConnectionEvent(_In_ const SCAMALERT_CONNECTION_DECISION* Decision);
```

- [ ] **Step 2: Implement event queue**

Create `native/ScamAlert.WfpDriver/EventQueue.cpp`:

```cpp
#include "EventQueue.h"

static LIST_ENTRY g_EventQueue;
static KSPIN_LOCK g_EventQueueLock;
static bool g_EventQueueInitialized = false;

NTSTATUS ScamAlertInitializeEventQueue()
{
    InitializeListHead(&g_EventQueue);
    KeInitializeSpinLock(&g_EventQueueLock);
    g_EventQueueInitialized = true;
    return STATUS_SUCCESS;
}

VOID ScamAlertDestroyEventQueue()
{
    if (!g_EventQueueInitialized)
    {
        return;
    }

    KIRQL oldIrql;
    KeAcquireSpinLock(&g_EventQueueLock, &oldIrql);
    while (!IsListEmpty(&g_EventQueue))
    {
        auto entry = RemoveHeadList(&g_EventQueue);
        auto node = CONTAINING_RECORD(entry, SCAMALERT_EVENT_NODE, Link);
        ExFreePoolWithTag(node, 'aScS');
    }
    KeReleaseSpinLock(&g_EventQueueLock, oldIrql);
    g_EventQueueInitialized = false;
}

NTSTATUS ScamAlertQueueConnectionEvent(_In_ const SCAMALERT_CONNECTION_EVENT* Event)
{
    auto node = static_cast<SCAMALERT_EVENT_NODE*>(
        ExAllocatePool2(POOL_FLAG_NON_PAGED, sizeof(SCAMALERT_EVENT_NODE), 'aScS'));
    if (node == nullptr)
    {
        return STATUS_INSUFFICIENT_RESOURCES;
    }

    RtlCopyMemory(&node->Event, Event, sizeof(SCAMALERT_CONNECTION_EVENT));

    KIRQL oldIrql;
    KeAcquireSpinLock(&g_EventQueueLock, &oldIrql);
    InsertTailList(&g_EventQueue, &node->Link);
    KeReleaseSpinLock(&g_EventQueueLock, oldIrql);
    return STATUS_SUCCESS;
}

NTSTATUS ScamAlertPopConnectionEvent(_Out_ SCAMALERT_CONNECTION_EVENT* Event)
{
    KIRQL oldIrql;
    KeAcquireSpinLock(&g_EventQueueLock, &oldIrql);
    if (IsListEmpty(&g_EventQueue))
    {
        KeReleaseSpinLock(&g_EventQueueLock, oldIrql);
        return STATUS_NO_MORE_ENTRIES;
    }

    auto entry = RemoveHeadList(&g_EventQueue);
    KeReleaseSpinLock(&g_EventQueueLock, oldIrql);

    auto node = CONTAINING_RECORD(entry, SCAMALERT_EVENT_NODE, Link);
    RtlCopyMemory(Event, &node->Event, sizeof(SCAMALERT_CONNECTION_EVENT));
    ExFreePoolWithTag(node, 'aScS');
    return STATUS_SUCCESS;
}

NTSTATUS ScamAlertCompleteConnectionEvent(_In_ const SCAMALERT_CONNECTION_DECISION* Decision)
{
    UNREFERENCED_PARAMETER(Decision);
    return STATUS_SUCCESS;
}
```

- [ ] **Step 3: Wire IOCTL dispatch**

Modify `native/ScamAlert.WfpDriver/Device.cpp` to:

- Include `EventQueue.h`.
- Set `DriverObject->MajorFunction[IRP_MJ_DEVICE_CONTROL]`.
- Call `ScamAlertInitializeEventQueue()` when device is created.
- Call `ScamAlertDestroyEventQueue()` on delete.
- For `IOCTL_SCAMALERT_GET_EVENT`, copy one queued `SCAMALERT_CONNECTION_EVENT` to the output buffer.
- For `IOCTL_SCAMALERT_COMPLETE_EVENT`, validate input size and call `ScamAlertCompleteConnectionEvent`.

Add this dispatch function:

```cpp
static NTSTATUS ScamAlertDeviceControl(
    _In_ PDEVICE_OBJECT DeviceObject,
    _Inout_ PIRP Irp)
{
    UNREFERENCED_PARAMETER(DeviceObject);

    auto stack = IoGetCurrentIrpStackLocation(Irp);
    auto code = stack->Parameters.DeviceIoControl.IoControlCode;
    auto inputLength = stack->Parameters.DeviceIoControl.InputBufferLength;
    auto outputLength = stack->Parameters.DeviceIoControl.OutputBufferLength;
    auto buffer = Irp->AssociatedIrp.SystemBuffer;
    NTSTATUS status = STATUS_INVALID_DEVICE_REQUEST;
    ULONG_PTR information = 0;

    if (code == IOCTL_SCAMALERT_GET_EVENT)
    {
        if (outputLength < sizeof(SCAMALERT_CONNECTION_EVENT))
        {
            status = STATUS_BUFFER_TOO_SMALL;
        }
        else
        {
            status = ScamAlertPopConnectionEvent(static_cast<SCAMALERT_CONNECTION_EVENT*>(buffer));
            if (NT_SUCCESS(status))
            {
                information = sizeof(SCAMALERT_CONNECTION_EVENT);
            }
        }
    }
    else if (code == IOCTL_SCAMALERT_COMPLETE_EVENT)
    {
        if (inputLength < sizeof(SCAMALERT_CONNECTION_DECISION))
        {
            status = STATUS_BUFFER_TOO_SMALL;
        }
        else
        {
            status = ScamAlertCompleteConnectionEvent(static_cast<SCAMALERT_CONNECTION_DECISION*>(buffer));
        }
    }

    Irp->IoStatus.Status = status;
    Irp->IoStatus.Information = information;
    IoCompleteRequest(Irp, IO_NO_INCREMENT);
    return status;
}
```

- [ ] **Step 4: Build driver**

Run in Developer PowerShell:

```powershell
scripts/driver/build-driver.ps1
```

Expected: driver builds.

- [ ] **Step 5: Commit IOCTL queue**

Run:

```powershell
git add native/ScamAlert.WfpDriver
git commit -m "feat: add driver IOCTL event queue"
```

Expected: commit succeeds.

## Task 7: Observe-Only WFP Registration

**Files:**
- Create: `native/ScamAlert.WfpDriver/WfpMonitor.h`
- Create: `native/ScamAlert.WfpDriver/WfpMonitor.cpp`
- Modify: `native/ScamAlert.WfpDriver/Driver.cpp`

- [ ] **Step 1: Add WFP monitor header**

Create `native/ScamAlert.WfpDriver/WfpMonitor.h`:

```cpp
#pragma once

#include <ntddk.h>

NTSTATUS ScamAlertStartWfpMonitor(_In_ PDEVICE_OBJECT DeviceObject);
VOID ScamAlertStopWfpMonitor();
```

- [ ] **Step 2: Implement observe-only classify**

Create `native/ScamAlert.WfpDriver/WfpMonitor.cpp` with these responsibilities:

- Register IPv4 and IPv6 callouts with `FwpsCalloutRegister0`.
- Open the filter engine with `FwpmEngineOpen0`.
- Add provider, sublayer, callouts, and filters for ALE receive/accept V4/V6.
- Filter inbound TCP local ports 3389, 22, and 23.
- In classify, ignore reauthorization.
- In classify, build `SCAMALERT_CONNECTION_EVENT` and call `ScamAlertQueueConnectionEvent`.
- Return `FWP_ACTION_PERMIT` in observe-only mode.

Use these GUID declarations:

```cpp
// {585493A7-CF45-4551-ABCF-111BA6007130}
DEFINE_GUID(SCAMALERT_CALLOUT_RECV_ACCEPT_V4,
    0x585493a7, 0xcf45, 0x4551, 0xab, 0xcf, 0x11, 0x1b, 0xa6, 0x0, 0x71, 0x30);

// {BA321121-D6AF-4AA9-907C-F365D7C2684A}
DEFINE_GUID(SCAMALERT_CALLOUT_RECV_ACCEPT_V6,
    0xba321121, 0xd6af, 0x4aa9, 0x90, 0x7c, 0xf3, 0x65, 0xd7, 0xc2, 0x68, 0x4a);

// {653537B3-4364-4E17-A99C-45F31AF2B9ED}
DEFINE_GUID(SCAMALERT_SUBLAYER,
    0x653537b3, 0x4364, 0x4e17, 0xa9, 0x9c, 0x45, 0xf3, 0x1a, 0xf2, 0xb9, 0xed);
```

Use this classify support code before the V4/V6 classify functions:

```cpp
static volatile LONG64 g_NextEventId = 0;

static ULONGLONG ScamAlertUnixTimeMilliseconds()
{
    LARGE_INTEGER systemTime;
    KeQuerySystemTimePrecise(&systemTime);
    constexpr LONGLONG UnixEpochOffsetTicks = 116444736000000000LL;
    return static_cast<ULONGLONG>((systemTime.QuadPart - UnixEpochOffsetTicks) / 10000);
}

static VOID ScamAlertWriteEventId(_Out_writes_bytes_(16) UINT8* eventId)
{
    const auto sequence = static_cast<ULONGLONG>(InterlockedIncrement64(&g_NextEventId));
    LARGE_INTEGER systemTime;
    KeQuerySystemTimePrecise(&systemTime);

    RtlZeroMemory(eventId, 16);
    RtlCopyMemory(eventId, &sequence, sizeof(sequence));
    RtlCopyMemory(eventId + sizeof(sequence), &systemTime.QuadPart, sizeof(systemTime.QuadPart));
}

static bool ScamAlertTryGetService(UINT16 localPort, _Out_ SCAMALERT_PROTECTED_SERVICE* service)
{
    switch (localPort)
    {
    case 3389:
        *service = ScamAlertServiceRdp;
        return true;
    case 22:
        *service = ScamAlertServiceSsh;
        return true;
    case 23:
        *service = ScamAlertServiceTelnet;
        return true;
    default:
        return false;
    }
}

static NTSTATUS ScamAlertWriteIpv4Source(UINT32 sourceAddressHostOrder, _Out_writes_(SCAMALERT_MAX_IP_CHARS) WCHAR* sourceIp)
{
    const auto sourceAddressNetworkOrder = RtlUlongByteSwap(sourceAddressHostOrder);
    return RtlStringCchPrintfW(
        sourceIp,
        SCAMALERT_MAX_IP_CHARS,
        L"%u.%u.%u.%u",
        static_cast<unsigned>((sourceAddressNetworkOrder >> 24) & 0xff),
        static_cast<unsigned>((sourceAddressNetworkOrder >> 16) & 0xff),
        static_cast<unsigned>((sourceAddressNetworkOrder >> 8) & 0xff),
        static_cast<unsigned>(sourceAddressNetworkOrder & 0xff));
}

static NTSTATUS ScamAlertWriteIpv6Source(const FWP_BYTE_ARRAY16* sourceAddress, _Out_writes_(SCAMALERT_MAX_IP_CHARS) WCHAR* sourceIp)
{
    if (sourceAddress == nullptr)
    {
        return STATUS_INVALID_PARAMETER;
    }

    const auto b = sourceAddress->byteArray16;
    return RtlStringCchPrintfW(
        sourceIp,
        SCAMALERT_MAX_IP_CHARS,
        L"%02x%02x:%02x%02x:%02x%02x:%02x%02x:%02x%02x:%02x%02x:%02x%02x:%02x%02x",
        b[0], b[1], b[2], b[3],
        b[4], b[5], b[6], b[7],
        b[8], b[9], b[10], b[11],
        b[12], b[13], b[14], b[15]);
}

static bool ScamAlertIsReauthorize(const FWPS_INCOMING_METADATA_VALUES0* inMetaValues)
{
    return (inMetaValues->currentMetadataValues & FWPS_METADATA_FIELD_FLAGS) != 0 &&
        (inMetaValues->flags & FWP_CONDITION_FLAG_IS_REAUTHORIZE) != 0;
}

static VOID NTAPI ScamAlertClassifyRecvAcceptV4(
    const FWPS_INCOMING_VALUES0* inFixedValues,
    const FWPS_INCOMING_METADATA_VALUES0* inMetaValues,
    void* layerData,
    const FWPS_FILTER0* filter,
    UINT64 flowContext,
    FWPS_CLASSIFY_OUT0* classifyOut)
{
    UNREFERENCED_PARAMETER(layerData);
    UNREFERENCED_PARAMETER(filter);
    UNREFERENCED_PARAMETER(flowContext);

    if ((classifyOut->rights & FWPS_RIGHT_ACTION_WRITE) == 0)
    {
        return;
    }

    classifyOut->actionType = FWP_ACTION_PERMIT;

    if (ScamAlertIsReauthorize(inMetaValues))
    {
        return;
    }

    const auto localPort = inFixedValues
        ->incomingValue[FWPS_FIELD_ALE_AUTH_RECV_ACCEPT_V4_IP_LOCAL_PORT]
        .value.uint16;

    SCAMALERT_PROTECTED_SERVICE service;
    if (!ScamAlertTryGetService(localPort, &service))
    {
        return;
    }

    SCAMALERT_CONNECTION_EVENT event = {};
    ScamAlertWriteEventId(event.EventId);
    event.OccurredAtUnixTimeMilliseconds = ScamAlertUnixTimeMilliseconds();
    event.DestinationPort = localPort;
    event.ProtectedService = service;

    const auto sourceAddress = inFixedValues
        ->incomingValue[FWPS_FIELD_ALE_AUTH_RECV_ACCEPT_V4_IP_REMOTE_ADDRESS]
        .value.uint32;

    if (NT_SUCCESS(ScamAlertWriteIpv4Source(sourceAddress, event.SourceIp)))
    {
        ScamAlertQueueConnectionEvent(&event);
    }
}

static VOID NTAPI ScamAlertClassifyRecvAcceptV6(
    const FWPS_INCOMING_VALUES0* inFixedValues,
    const FWPS_INCOMING_METADATA_VALUES0* inMetaValues,
    void* layerData,
    const FWPS_FILTER0* filter,
    UINT64 flowContext,
    FWPS_CLASSIFY_OUT0* classifyOut)
{
    UNREFERENCED_PARAMETER(layerData);
    UNREFERENCED_PARAMETER(filter);
    UNREFERENCED_PARAMETER(flowContext);

    if ((classifyOut->rights & FWPS_RIGHT_ACTION_WRITE) == 0)
    {
        return;
    }

    classifyOut->actionType = FWP_ACTION_PERMIT;

    if (ScamAlertIsReauthorize(inMetaValues))
    {
        return;
    }

    const auto localPort = inFixedValues
        ->incomingValue[FWPS_FIELD_ALE_AUTH_RECV_ACCEPT_V6_IP_LOCAL_PORT]
        .value.uint16;

    SCAMALERT_PROTECTED_SERVICE service;
    if (!ScamAlertTryGetService(localPort, &service))
    {
        return;
    }

    SCAMALERT_CONNECTION_EVENT event = {};
    ScamAlertWriteEventId(event.EventId);
    event.OccurredAtUnixTimeMilliseconds = ScamAlertUnixTimeMilliseconds();
    event.DestinationPort = localPort;
    event.ProtectedService = service;

    const auto sourceAddress = inFixedValues
        ->incomingValue[FWPS_FIELD_ALE_AUTH_RECV_ACCEPT_V6_IP_REMOTE_ADDRESS]
        .value.byteArray16;

    if (NT_SUCCESS(ScamAlertWriteIpv6Source(sourceAddress, event.SourceIp)))
    {
        ScamAlertQueueConnectionEvent(&event);
    }
}
```

Include `<fwpsk.h>`, `<fwpmk.h>`, `<ntstrsafe.h>`, `EventQueue.h`, and `..\ScamAlert.Driver.Shared\ScamAlertDriverIoctl.h`. VM validation in Task 8 must confirm that IPv4 addresses are not byte-reversed; if they are reversed on the target WDK/runtime combination, remove `RtlUlongByteSwap` in `ScamAlertWriteIpv4Source` and rerun the validation runbook before committing.

- [ ] **Step 3: Start and stop WFP monitor from driver**

Modify `native/ScamAlert.WfpDriver/Driver.cpp`:

- Include `WfpMonitor.h`.
- Call `ScamAlertStartWfpMonitor` after device creation succeeds.
- In unload, call `ScamAlertStopWfpMonitor` before deleting the device.

- [ ] **Step 4: Build driver**

Run:

```powershell
scripts/driver/build-driver.ps1
```

Expected: driver builds and links with `Fwpkclnt.lib`.

- [ ] **Step 5: Commit observe-only WFP monitor**

Run:

```powershell
git add native/ScamAlert.WfpDriver
git commit -m "feat: observe protected inbound WFP attempts"
```

Expected: commit succeeds.

## Task 8: Driver Install And Observe-Only VM Validation

**Files:**
- Create: `scripts/driver/install-driver.ps1`
- Create: `scripts/driver/uninstall-driver.ps1`
- Create: `docs/driver/observe-only-validation.md`

- [ ] **Step 1: Add install script**

Create `scripts/driver/install-driver.ps1`:

```powershell
$ErrorActionPreference = 'Stop'

$driverDir = Resolve-Path 'native\ScamAlert.WfpDriver\x64\Debug'
$inf = Get-ChildItem -Path $driverDir -Filter '*.inf' | Select-Object -First 1

if (-not $inf) {
    throw 'Driver INF not found. Build the driver first.'
}

pnputil /add-driver $inf.FullName /install
sc.exe start ScamAlertWfp
```

- [ ] **Step 2: Add uninstall script**

Create `scripts/driver/uninstall-driver.ps1`:

```powershell
$ErrorActionPreference = 'Continue'

sc.exe stop ScamAlertWfp
sc.exe delete ScamAlertWfp

pnputil /enum-drivers | Select-String -Pattern 'ScamAlert' -Context 4,4
```

- [ ] **Step 3: Write validation runbook**

Create `docs/driver/observe-only-validation.md`:

````markdown
# Observe-Only WFP Validation

Run this only inside a test VM.

## Start User-Mode Components

```powershell
dotnet run --project src/ScamAlert.Broker/ScamAlert.Broker.csproj
dotnet run --project src/ScamAlert.Tray/ScamAlert.Tray.csproj
dotnet run --project src/ScamAlert.DriverBridge/ScamAlert.DriverBridge.csproj
```

## Install Driver

In Administrator PowerShell:

```powershell
scripts/driver/install-driver.ps1
```

## Generate Test Traffic

From another machine on the same network:

```powershell
Test-NetConnection <vm-ip> -Port 3389
Test-NetConnection <vm-ip> -Port 22
Test-NetConnection <vm-ip> -Port 23
```

Expected:

- ScamAlert tray prompt appears for protected inbound attempts.
- `%LOCALAPPDATA%\ScamAlert\signals.jsonl` records `ObservedInboundAttempt`.
- Observe-only driver does not block traffic.

## Cleanup

```powershell
scripts/driver/uninstall-driver.ps1
```
````

- [ ] **Step 4: Run VM validation**

Run the runbook in `docs/driver/observe-only-validation.md`.

Expected: real inbound protected attempts produce tray prompts and local signals.

- [ ] **Step 5: Commit validation scripts**

Run:

```powershell
git add scripts/driver docs/driver/observe-only-validation.md
git commit -m "docs: add observe-only driver validation"
```

Expected: commit succeeds.

## Task 9: Enforcement Follow-Up Gate

**Files:**
- Create: `docs/driver/pend-and-decide-design-notes.md`

- [ ] **Step 1: Record enforcement requirements**

Create `docs/driver/pend-and-decide-design-notes.md`:

```markdown
# Pend-And-Decide Enforcement Notes

Observe-only validation must pass before enabling WFP enforcement.

## Enforcement Requirements

- Use `FwpsPendOperation0` only for initial ALE authorization.
- Do not pend reauthorization events.
- Bound pending authorization time in the driver.
- Apply persisted fail behavior if bridge or broker does not respond.
- Default fail behavior remains allow.
- At `ALE_AUTH_RECV_ACCEPT`, account for packet clone/reinject requirements documented for pended receive/accept operations.
- Keep cloud lookup out of kernel mode.
- Keep UI out of kernel mode.

## Required Next Plan

The next plan must implement:

- Pending event table keyed by event ID.
- Inverted call or completion IOCTL for decisions.
- Driver-side timeout timer.
- `FwpsPendOperation0` / `FwpsCompleteOperation0`.
- Packet clone/reinject handling for receive/accept.
- Enforcement tests in a VM.
```

- [ ] **Step 2: Commit enforcement notes**

Run:

```powershell
git add docs/driver/pend-and-decide-design-notes.md
git commit -m "docs: capture WFP enforcement gate"
```

Expected: commit succeeds.

## Final Verification

- [ ] **Step 1: Run managed verification**

Run:

```powershell
dotnet build ScamAlert.sln
dotnet test ScamAlert.sln
```

Expected: build and tests pass.

- [ ] **Step 2: Run driver verification in VM**

Run:

```powershell
scripts/driver/check-driver-prereqs.ps1
scripts/driver/build-driver.ps1
```

Expected: both pass on the WDK VM.

- [ ] **Step 3: Confirm observe-only behavior**

Run the validation runbook and confirm:

```text
Real inbound 3389/22/23 attempt -> WFP driver event -> DriverBridge -> Broker -> Tray prompt -> JSONL signal
```

- [ ] **Step 4: Inspect git status**

Run:

```powershell
git status --short
```

Expected: no uncommitted source changes. `ScamAlert.slnLaunch` may remain untracked if Visual Studio generated it locally and the user wants to keep it machine-local.
