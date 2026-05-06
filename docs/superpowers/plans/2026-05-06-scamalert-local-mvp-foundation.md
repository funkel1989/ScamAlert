# ScamAlert Local MVP Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the local ScamAlert foundation for inbound RDP/SSH/Telnet protection: shared contracts, broker policy, local signal logging, remembered IP rules, tray prompts, and a driver simulator that exercises the same decision path the WFP driver will use.

**Architecture:** The first testable slice uses a .NET broker service as the policy owner, a Windows tray app for user decisions, JSONL signal logging for the cloud-reporting stub, and a named-pipe driver simulator for inbound connection attempts. The real WFP driver remains behind the same driver-event/decision boundary so the low-level native integration can replace the simulator without changing policy, UI, or signal logic.

**Tech Stack:** .NET 8, C# nullable reference types, xUnit, `System.Text.Json`, `Microsoft.Extensions.Hosting`, Windows Forms tray UI, Windows named pipes.

---

## Scope Check

The approved spec spans multiple subsystems: broker, tray UI, local persistence, signal reporting, installer policy, and a C++ WFP driver. This plan implements the first working vertical slice without a kernel driver. It validates all local product behavior with a driver simulator, then documents the native-driver handoff contract.

The WFP driver binary, driver signing, and installer driver deployment should receive their own implementation plan after this foundation passes verification.

## File Structure

- `ScamAlert.sln` - solution file.
- `.gitignore` - excludes build output, user files, logs, and local signal data.
- `Directory.Build.props` - common .NET build settings.
- `src/ScamAlert.Contracts/` - shared DTOs, enums, JSON options, and pipe messages.
- `src/ScamAlert.Core/` - policy engine, remembered rules, signal sink, broker orchestration, testable interfaces.
- `src/ScamAlert.Broker/` - hosted broker process, named-pipe server for driver events, prompt client for tray UI.
- `src/ScamAlert.Tray/` - Windows tray UI and named-pipe prompt server.
- `tools/ScamAlert.DriverSimulator/` - console tool that simulates protected inbound connection attempts.
- `tests/ScamAlert.Core.Tests/` - unit and integration tests for contracts, policy, persistence, signal logging, and broker flow.
- `docs/driver/wfp-integration-contract.md` - driver handoff contract for the later C++ WFP implementation.

## Task 0: Repository And Solution Scaffold

**Files:**
- Create: `.gitignore`
- Create: `Directory.Build.props`
- Create: `ScamAlert.sln`
- Create: `src/ScamAlert.Contracts/ScamAlert.Contracts.csproj`
- Create: `src/ScamAlert.Core/ScamAlert.Core.csproj`
- Create: `src/ScamAlert.Broker/ScamAlert.Broker.csproj`
- Create: `src/ScamAlert.Tray/ScamAlert.Tray.csproj`
- Create: `tools/ScamAlert.DriverSimulator/ScamAlert.DriverSimulator.csproj`
- Create: `tests/ScamAlert.Core.Tests/ScamAlert.Core.Tests.csproj`

- [ ] **Step 1: Initialize git and write common repository files**

Run:

```powershell
git init
```

Create `.gitignore`:

```gitignore
bin/
obj/
.vs/
.vscode/
*.user
*.suo
TestResults/
coverage/
artifacts/
data/*.jsonl
data/*.json
logs/
```

Create `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latestMajor</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Create solution and projects**

Run:

```powershell
dotnet new sln -n ScamAlert
dotnet new classlib -n ScamAlert.Contracts -o src/ScamAlert.Contracts
dotnet new classlib -n ScamAlert.Core -o src/ScamAlert.Core
dotnet new worker -n ScamAlert.Broker -o src/ScamAlert.Broker
dotnet new winforms -n ScamAlert.Tray -o src/ScamAlert.Tray --framework net8.0-windows
dotnet new console -n ScamAlert.DriverSimulator -o tools/ScamAlert.DriverSimulator
dotnet new xunit -n ScamAlert.Core.Tests -o tests/ScamAlert.Core.Tests
dotnet sln ScamAlert.sln add src/ScamAlert.Contracts/ScamAlert.Contracts.csproj
dotnet sln ScamAlert.sln add src/ScamAlert.Core/ScamAlert.Core.csproj
dotnet sln ScamAlert.sln add src/ScamAlert.Broker/ScamAlert.Broker.csproj
dotnet sln ScamAlert.sln add src/ScamAlert.Tray/ScamAlert.Tray.csproj
dotnet sln ScamAlert.sln add tools/ScamAlert.DriverSimulator/ScamAlert.DriverSimulator.csproj
dotnet sln ScamAlert.sln add tests/ScamAlert.Core.Tests/ScamAlert.Core.Tests.csproj
```

Expected: each `dotnet new` command reports successful template creation and each `dotnet sln add` command reports a project added.

- [ ] **Step 3: Add project references**

Run:

```powershell
dotnet add src/ScamAlert.Core/ScamAlert.Core.csproj reference src/ScamAlert.Contracts/ScamAlert.Contracts.csproj
dotnet add src/ScamAlert.Broker/ScamAlert.Broker.csproj reference src/ScamAlert.Contracts/ScamAlert.Contracts.csproj
dotnet add src/ScamAlert.Broker/ScamAlert.Broker.csproj reference src/ScamAlert.Core/ScamAlert.Core.csproj
dotnet add src/ScamAlert.Tray/ScamAlert.Tray.csproj reference src/ScamAlert.Contracts/ScamAlert.Contracts.csproj
dotnet add tools/ScamAlert.DriverSimulator/ScamAlert.DriverSimulator.csproj reference src/ScamAlert.Contracts/ScamAlert.Contracts.csproj
dotnet add tests/ScamAlert.Core.Tests/ScamAlert.Core.Tests.csproj reference src/ScamAlert.Contracts/ScamAlert.Contracts.csproj
dotnet add tests/ScamAlert.Core.Tests/ScamAlert.Core.Tests.csproj reference src/ScamAlert.Core/ScamAlert.Core.csproj
```

Expected: each command reports a reference added.

- [ ] **Step 4: Verify clean scaffold**

Run:

```powershell
dotnet build ScamAlert.sln
dotnet test tests/ScamAlert.Core.Tests/ScamAlert.Core.Tests.csproj
```

Expected: build succeeds and the generated xUnit sample test passes.

- [ ] **Step 5: Commit scaffold**

Run:

```powershell
git add .gitignore Directory.Build.props ScamAlert.sln src tools tests docs
git commit -m "chore: scaffold ScamAlert solution"
```

Expected: commit succeeds with the scaffolded files.

## Task 1: Shared Remote Access Contracts

**Files:**
- Create: `src/ScamAlert.Contracts/RemoteAccessContracts.cs`
- Create: `tests/ScamAlert.Core.Tests/Contracts/RemoteAccessContractsTests.cs`
- Modify: remove generated `Class1.cs` from `src/ScamAlert.Contracts/`
- Modify: remove generated `UnitTest1.cs` from `tests/ScamAlert.Core.Tests/`

- [ ] **Step 1: Write failing contract tests**

Create `tests/ScamAlert.Core.Tests/Contracts/RemoteAccessContractsTests.cs`:

```csharp
using System.Text.Json;
using ScamAlert.Contracts;

namespace ScamAlert.Core.Tests.Contracts;

public sealed class RemoteAccessContractsTests
{
    [Theory]
    [InlineData(3389, ProtectedService.Rdp)]
    [InlineData(22, ProtectedService.Ssh)]
    [InlineData(23, ProtectedService.Telnet)]
    public void TryFromPortMapsProtectedPorts(int port, ProtectedService expected)
    {
        var mapped = ProtectedServiceMap.TryFromPort(port, out var service);

        Assert.True(mapped);
        Assert.Equal(expected, service);
    }

    [Fact]
    public void TryFromPortRejectsUnprotectedPorts()
    {
        var mapped = ProtectedServiceMap.TryFromPort(443, out _);

        Assert.False(mapped);
    }

    [Fact]
    public void ObservedAttemptSerializesWithCamelCaseEventType()
    {
        var signal = new ObservedInboundAttemptSignal(
            EventId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            OccurredAt: DateTimeOffset.Parse("2026-05-06T12:00:00Z"),
            SourceIp: "203.0.113.10",
            DestinationPort: 3389,
            ProtectedService: ProtectedService.Rdp,
            LocalPolicyMode: TimeoutPolicy.AllowOnTimeout,
            DecisionStatus: DecisionStatus.Pending);

        var json = JsonSerializer.Serialize(signal, SignalJson.Options);

        Assert.Contains("\"eventType\":\"ObservedInboundAttempt\"", json);
        Assert.Contains("\"sourceIp\":\"203.0.113.10\"", json);
        Assert.Contains("\"protectedService\":\"rdp\"", json);
    }
}
```

Delete generated files:

```powershell
Remove-Item -LiteralPath src/ScamAlert.Contracts/Class1.cs
Remove-Item -LiteralPath tests/ScamAlert.Core.Tests/UnitTest1.cs
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test tests/ScamAlert.Core.Tests/ScamAlert.Core.Tests.csproj --filter RemoteAccessContractsTests
```

Expected: build fails because `ProtectedService`, `ProtectedServiceMap`, and signal types do not exist.

- [ ] **Step 3: Add shared contracts**

Create `src/ScamAlert.Contracts/RemoteAccessContracts.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScamAlert.Contracts;

public enum ProtectedService
{
    Rdp,
    Ssh,
    Telnet
}

public enum TimeoutPolicy
{
    AllowOnTimeout,
    BlockOnTimeout
}

public enum DecisionStatus
{
    Pending,
    Remembered,
    UserSelected,
    TimedOut
}

public enum UserDecisionKind
{
    AllowOnce,
    BlockOnce
}

public enum DriverDecisionKind
{
    Allow,
    Block
}

public static class ProtectedServiceMap
{
    public static bool TryFromPort(int destinationPort, out ProtectedService service)
    {
        service = destinationPort switch
        {
            3389 => ProtectedService.Rdp,
            22 => ProtectedService.Ssh,
            23 => ProtectedService.Telnet,
            _ => default
        };

        return destinationPort is 3389 or 22 or 23;
    }
}

public sealed record ProtectedConnectionAttempt(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string SourceIp,
    int DestinationPort,
    ProtectedService ProtectedService);

public sealed record ObservedInboundAttemptSignal(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string SourceIp,
    int DestinationPort,
    ProtectedService ProtectedService,
    TimeoutPolicy LocalPolicyMode,
    DecisionStatus DecisionStatus)
{
    public string EventType => "ObservedInboundAttempt";
}

public sealed record UserDecisionUpdatedSignal(
    Guid EventId,
    Guid ObservedEventId,
    DateTimeOffset OccurredAt,
    string SourceIp,
    UserDecisionKind Decision,
    bool Remembered,
    string Reason)
{
    public string EventType => "UserDecisionUpdated";
}

public sealed record DecisionPromptRequest(
    Guid ObservedEventId,
    DateTimeOffset OccurredAt,
    string SourceIp,
    int DestinationPort,
    ProtectedService ProtectedService,
    TimeoutPolicy LocalPolicyMode,
    int TimeoutSeconds);

public sealed record DecisionPromptResponse(
    Guid ObservedEventId,
    UserDecisionKind Decision,
    bool Remember);

public sealed record DriverDecisionResponse(
    Guid ObservedEventId,
    DriverDecisionKind Decision,
    string Reason);

public static class SignalJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}
```

- [ ] **Step 4: Verify contracts**

Run:

```powershell
dotnet test tests/ScamAlert.Core.Tests/ScamAlert.Core.Tests.csproj --filter RemoteAccessContractsTests
```

Expected: all `RemoteAccessContractsTests` pass.

- [ ] **Step 5: Commit contracts**

Run:

```powershell
git add src/ScamAlert.Contracts tests/ScamAlert.Core.Tests
git commit -m "feat: add remote access contracts"
```

Expected: commit succeeds.

## Task 2: JSONL Signal Sink

**Files:**
- Create: `src/ScamAlert.Core/Signals/ISignalSink.cs`
- Create: `src/ScamAlert.Core/Signals/JsonlSignalSink.cs`
- Create: `tests/ScamAlert.Core.Tests/Signals/JsonlSignalSinkTests.cs`
- Modify: remove generated `Class1.cs` from `src/ScamAlert.Core/`

- [ ] **Step 1: Write failing signal sink tests**

Create `tests/ScamAlert.Core.Tests/Signals/JsonlSignalSinkTests.cs`:

```csharp
using ScamAlert.Contracts;
using ScamAlert.Core.Signals;

namespace ScamAlert.Core.Tests.Signals;

public sealed class JsonlSignalSinkTests
{
    [Fact]
    public async Task AppendAsyncWritesOneJsonObjectPerLine()
    {
        var path = Path.Combine(Path.GetTempPath(), $"scamalert-{Guid.NewGuid():N}.jsonl");
        var sink = new JsonlSignalSink(path);
        var observed = new ObservedInboundAttemptSignal(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            DateTimeOffset.Parse("2026-05-06T12:00:00Z"),
            "203.0.113.10",
            3389,
            ProtectedService.Rdp,
            TimeoutPolicy.AllowOnTimeout,
            DecisionStatus.Pending);
        var decision = new UserDecisionUpdatedSignal(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            observed.EventId,
            DateTimeOffset.Parse("2026-05-06T12:00:05Z"),
            "203.0.113.10",
            UserDecisionKind.BlockOnce,
            false,
            "userSelected");

        await sink.AppendAsync(observed, CancellationToken.None);
        await sink.AppendAsync(decision, CancellationToken.None);

        var lines = await File.ReadAllLinesAsync(path);
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"eventType\":\"ObservedInboundAttempt\"", lines[0]);
        Assert.Contains("\"eventType\":\"UserDecisionUpdated\"", lines[1]);
    }
}
```

Delete generated file:

```powershell
Remove-Item -LiteralPath src/ScamAlert.Core/Class1.cs
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests/ScamAlert.Core.Tests/ScamAlert.Core.Tests.csproj --filter JsonlSignalSinkTests
```

Expected: build fails because `ISignalSink` and `JsonlSignalSink` do not exist.

- [ ] **Step 3: Implement signal sink**

Create `src/ScamAlert.Core/Signals/ISignalSink.cs`:

```csharp
namespace ScamAlert.Core.Signals;

public interface ISignalSink
{
    Task AppendAsync<TSignal>(TSignal signal, CancellationToken cancellationToken);
}
```

Create `src/ScamAlert.Core/Signals/JsonlSignalSink.cs`:

```csharp
using System.Text.Json;
using ScamAlert.Contracts;

namespace ScamAlert.Core.Signals;

public sealed class JsonlSignalSink(string path) : ISignalSink
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task AppendAsync<TSignal>(TSignal signal, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var line = JsonSerializer.Serialize(signal, SignalJson.Options);

        await gate.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(path, line + Environment.NewLine, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }
}
```

- [ ] **Step 4: Verify signal sink**

Run:

```powershell
dotnet test tests/ScamAlert.Core.Tests/ScamAlert.Core.Tests.csproj --filter JsonlSignalSinkTests
```

Expected: all `JsonlSignalSinkTests` pass.

- [ ] **Step 5: Commit signal sink**

Run:

```powershell
git add src/ScamAlert.Core tests/ScamAlert.Core.Tests
git commit -m "feat: add local JSONL signal sink"
```

Expected: commit succeeds.

## Task 3: Settings And Remembered IP Rules

**Files:**
- Create: `src/ScamAlert.Core/Configuration/ProtectionSettings.cs`
- Create: `src/ScamAlert.Core/Configuration/IProtectionSettingsStore.cs`
- Create: `src/ScamAlert.Core/Configuration/FileProtectionSettingsStore.cs`
- Create: `src/ScamAlert.Core/Rules/RememberedIpRule.cs`
- Create: `src/ScamAlert.Core/Rules/IRememberedRuleStore.cs`
- Create: `src/ScamAlert.Core/Rules/FileRememberedRuleStore.cs`
- Create: `tests/ScamAlert.Core.Tests/Rules/FileRememberedRuleStoreTests.cs`
- Create: `tests/ScamAlert.Core.Tests/Configuration/FileProtectionSettingsStoreTests.cs`

- [ ] **Step 1: Write failing remembered-rule tests**

Create `tests/ScamAlert.Core.Tests/Rules/FileRememberedRuleStoreTests.cs`:

```csharp
using ScamAlert.Contracts;
using ScamAlert.Core.Rules;

namespace ScamAlert.Core.Tests.Rules;

public sealed class FileRememberedRuleStoreTests
{
    [Fact]
    public async Task UpsertAsyncStoresRuleBySourceIpAcrossProtectedServices()
    {
        var path = Path.Combine(Path.GetTempPath(), $"scamalert-rules-{Guid.NewGuid():N}.json");
        var store = new FileRememberedRuleStore(path);
        var rule = new RememberedIpRule(
            SourceIp: "203.0.113.10",
            Decision: DriverDecisionKind.Block,
            CreatedAt: DateTimeOffset.Parse("2026-05-06T12:00:00Z"),
            UpdatedAt: DateTimeOffset.Parse("2026-05-06T12:00:00Z"));

        await store.UpsertAsync(rule, CancellationToken.None);

        var rdp = await store.FindBySourceIpAsync("203.0.113.10", CancellationToken.None);
        var ssh = await store.FindBySourceIpAsync("203.0.113.10", CancellationToken.None);
        Assert.NotNull(rdp);
        Assert.NotNull(ssh);
        Assert.Equal(DriverDecisionKind.Block, rdp.Decision);
        Assert.Equal(DriverDecisionKind.Block, ssh.Decision);
    }
}
```

Create `tests/ScamAlert.Core.Tests/Configuration/FileProtectionSettingsStoreTests.cs`:

```csharp
using ScamAlert.Contracts;
using ScamAlert.Core.Configuration;

namespace ScamAlert.Core.Tests.Configuration;

public sealed class FileProtectionSettingsStoreTests
{
    [Fact]
    public async Task GetAsyncReturnsDefaultAllowPolicyWhenFileDoesNotExist()
    {
        var path = Path.Combine(Path.GetTempPath(), $"scamalert-settings-{Guid.NewGuid():N}.json");
        var store = new FileProtectionSettingsStore(path);

        var settings = await store.GetAsync(CancellationToken.None);

        Assert.Equal(TimeoutPolicy.AllowOnTimeout, settings.TimeoutPolicy);
        Assert.Equal(10, settings.PromptTimeoutSeconds);
    }

    [Fact]
    public async Task SaveAsyncPersistsConfiguredBlockPolicy()
    {
        var path = Path.Combine(Path.GetTempPath(), $"scamalert-settings-{Guid.NewGuid():N}.json");
        var store = new FileProtectionSettingsStore(path);
        var expected = new ProtectionSettings(TimeoutPolicy.BlockOnTimeout, 15);

        await store.SaveAsync(expected, CancellationToken.None);
        var actual = await store.GetAsync(CancellationToken.None);

        Assert.Equal(expected, actual);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests/ScamAlert.Core.Tests/ScamAlert.Core.Tests.csproj
```

Expected: build fails because settings store and rule store types do not exist.

- [ ] **Step 3: Implement settings and remembered rules**

Create `src/ScamAlert.Core/Configuration/ProtectionSettings.cs`:

```csharp
using ScamAlert.Contracts;

namespace ScamAlert.Core.Configuration;

public sealed record ProtectionSettings(
    TimeoutPolicy TimeoutPolicy,
    int PromptTimeoutSeconds)
{
    public static ProtectionSettings Default => new(TimeoutPolicy.AllowOnTimeout, 10);
}
```

Create `src/ScamAlert.Core/Configuration/IProtectionSettingsStore.cs`:

```csharp
namespace ScamAlert.Core.Configuration;

public interface IProtectionSettingsStore
{
    Task<ProtectionSettings> GetAsync(CancellationToken cancellationToken);

    Task SaveAsync(ProtectionSettings settings, CancellationToken cancellationToken);
}
```

Create `src/ScamAlert.Core/Configuration/FileProtectionSettingsStore.cs`:

```csharp
using System.Text.Json;
using ScamAlert.Contracts;

namespace ScamAlert.Core.Configuration;

public sealed class FileProtectionSettingsStore(string path) : IProtectionSettingsStore
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<ProtectionSettings> GetAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(path))
            {
                return ProtectionSettings.Default;
            }

            await using var stream = File.OpenRead(path);
            var settings = await JsonSerializer.DeserializeAsync<ProtectionSettings>(stream, SignalJson.Options, cancellationToken);
            return settings ?? ProtectionSettings.Default;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SaveAsync(ProtectionSettings settings, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(settings, SignalJson.Options);
            await File.WriteAllTextAsync(path, json, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }
}
```

Create `src/ScamAlert.Core/Rules/RememberedIpRule.cs`:

```csharp
using ScamAlert.Contracts;

namespace ScamAlert.Core.Rules;

public sealed record RememberedIpRule(
    string SourceIp,
    DriverDecisionKind Decision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
```

Create `src/ScamAlert.Core/Rules/IRememberedRuleStore.cs`:

```csharp
namespace ScamAlert.Core.Rules;

public interface IRememberedRuleStore
{
    Task<RememberedIpRule?> FindBySourceIpAsync(string sourceIp, CancellationToken cancellationToken);

    Task UpsertAsync(RememberedIpRule rule, CancellationToken cancellationToken);
}
```

Create `src/ScamAlert.Core/Rules/FileRememberedRuleStore.cs`:

```csharp
using System.Text.Json;
using ScamAlert.Contracts;

namespace ScamAlert.Core.Rules;

public sealed class FileRememberedRuleStore(string path) : IRememberedRuleStore
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<RememberedIpRule?> FindBySourceIpAsync(string sourceIp, CancellationToken cancellationToken)
    {
        var rules = await ReadRulesAsync(cancellationToken);
        return rules.FirstOrDefault(rule => string.Equals(rule.SourceIp, sourceIp, StringComparison.OrdinalIgnoreCase));
    }

    public async Task UpsertAsync(RememberedIpRule rule, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var rules = await ReadRulesWithoutLockAsync(cancellationToken);
            var index = rules.FindIndex(existing => string.Equals(existing.SourceIp, rule.SourceIp, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                rules[index] = rule;
            }
            else
            {
                rules.Add(rule);
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(rules, SignalJson.Options);
            await File.WriteAllTextAsync(path, json, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<List<RememberedIpRule>> ReadRulesAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await ReadRulesWithoutLockAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<List<RememberedIpRule>> ReadRulesWithoutLockAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        await using var stream = File.OpenRead(path);
        var rules = await JsonSerializer.DeserializeAsync<List<RememberedIpRule>>(stream, SignalJson.Options, cancellationToken);
        return rules ?? [];
    }
}
```

- [ ] **Step 4: Verify remembered rules**

Run:

```powershell
dotnet test tests/ScamAlert.Core.Tests/ScamAlert.Core.Tests.csproj
```

Expected: all `FileRememberedRuleStoreTests` and `FileProtectionSettingsStoreTests` pass.

- [ ] **Step 5: Commit settings and remembered rules**

Run:

```powershell
git add src/ScamAlert.Core tests/ScamAlert.Core.Tests
git commit -m "feat: add local policy stores"
```

Expected: commit succeeds.

## Task 4: Remote Access Policy Engine

**Files:**
- Create: `src/ScamAlert.Core/Policy/RemoteAccessPolicyEngine.cs`
- Create: `tests/ScamAlert.Core.Tests/Policy/RemoteAccessPolicyEngineTests.cs`

- [ ] **Step 1: Write failing policy tests**

Create `tests/ScamAlert.Core.Tests/Policy/RemoteAccessPolicyEngineTests.cs`:

```csharp
using ScamAlert.Contracts;
using ScamAlert.Core.Policy;
using ScamAlert.Core.Rules;

namespace ScamAlert.Core.Tests.Policy;

public sealed class RemoteAccessPolicyEngineTests
{
    [Fact]
    public void EvaluateRememberedRuleReturnsStoredDecision()
    {
        var engine = new RemoteAccessPolicyEngine();
        var attempt = NewAttempt();
        var rule = new RememberedIpRule(attempt.SourceIp, DriverDecisionKind.Block, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var decision = engine.EvaluateRememberedRule(attempt, rule);

        Assert.NotNull(decision);
        Assert.Equal(DriverDecisionKind.Block, decision.Decision);
        Assert.Equal("rememberedIpRule", decision.Reason);
    }

    [Theory]
    [InlineData(TimeoutPolicy.AllowOnTimeout, DriverDecisionKind.Allow)]
    [InlineData(TimeoutPolicy.BlockOnTimeout, DriverDecisionKind.Block)]
    public void ApplyTimeoutUsesConfiguredPolicy(TimeoutPolicy timeoutPolicy, DriverDecisionKind expected)
    {
        var engine = new RemoteAccessPolicyEngine();
        var attempt = NewAttempt();

        var decision = engine.ApplyTimeout(attempt, timeoutPolicy);

        Assert.Equal(expected, decision.Decision);
        Assert.Equal("timeoutPolicy", decision.Reason);
    }

    [Fact]
    public void BuildRememberedRuleStoresIpWideDecision()
    {
        var engine = new RemoteAccessPolicyEngine();
        var attempt = NewAttempt();
        var now = DateTimeOffset.Parse("2026-05-06T12:00:00Z");

        var rule = engine.BuildRememberedRule(attempt, UserDecisionKind.BlockOnce, now);

        Assert.Equal("203.0.113.10", rule.SourceIp);
        Assert.Equal(DriverDecisionKind.Block, rule.Decision);
    }

    private static ProtectedConnectionAttempt NewAttempt()
    {
        return new ProtectedConnectionAttempt(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            DateTimeOffset.Parse("2026-05-06T12:00:00Z"),
            "203.0.113.10",
            3389,
            ProtectedService.Rdp);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests/ScamAlert.Core.Tests/ScamAlert.Core.Tests.csproj --filter RemoteAccessPolicyEngineTests
```

Expected: build fails because `RemoteAccessPolicyEngine` does not exist.

- [ ] **Step 3: Implement policy engine**

Create `src/ScamAlert.Core/Policy/RemoteAccessPolicyEngine.cs`:

```csharp
using ScamAlert.Contracts;
using ScamAlert.Core.Rules;

namespace ScamAlert.Core.Policy;

public sealed class RemoteAccessPolicyEngine
{
    public DriverDecisionResponse? EvaluateRememberedRule(ProtectedConnectionAttempt attempt, RememberedIpRule? rule)
    {
        if (rule is null)
        {
            return null;
        }

        return new DriverDecisionResponse(attempt.EventId, rule.Decision, "rememberedIpRule");
    }

    public DriverDecisionResponse ApplyUserDecision(ProtectedConnectionAttempt attempt, UserDecisionKind decision)
    {
        var driverDecision = decision switch
        {
            UserDecisionKind.AllowOnce => DriverDecisionKind.Allow,
            UserDecisionKind.BlockOnce => DriverDecisionKind.Block,
            _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, "Unsupported user decision.")
        };

        return new DriverDecisionResponse(attempt.EventId, driverDecision, "userSelected");
    }

    public DriverDecisionResponse ApplyTimeout(ProtectedConnectionAttempt attempt, TimeoutPolicy timeoutPolicy)
    {
        var decision = timeoutPolicy switch
        {
            TimeoutPolicy.AllowOnTimeout => DriverDecisionKind.Allow,
            TimeoutPolicy.BlockOnTimeout => DriverDecisionKind.Block,
            _ => throw new ArgumentOutOfRangeException(nameof(timeoutPolicy), timeoutPolicy, "Unsupported timeout policy.")
        };

        return new DriverDecisionResponse(attempt.EventId, decision, "timeoutPolicy");
    }

    public RememberedIpRule BuildRememberedRule(ProtectedConnectionAttempt attempt, UserDecisionKind decision, DateTimeOffset now)
    {
        var driverDecision = decision == UserDecisionKind.AllowOnce ? DriverDecisionKind.Allow : DriverDecisionKind.Block;
        return new RememberedIpRule(attempt.SourceIp, driverDecision, now, now);
    }
}
```

- [ ] **Step 4: Verify policy engine**

Run:

```powershell
dotnet test tests/ScamAlert.Core.Tests/ScamAlert.Core.Tests.csproj --filter RemoteAccessPolicyEngineTests
```

Expected: all `RemoteAccessPolicyEngineTests` pass.

- [ ] **Step 5: Commit policy engine**

Run:

```powershell
git add src/ScamAlert.Core tests/ScamAlert.Core.Tests
git commit -m "feat: add remote access policy engine"
```

Expected: commit succeeds.

## Task 5: Broker Orchestration

**Files:**
- Create: `src/ScamAlert.Core/Broker/IConnectionDecisionPrompt.cs`
- Create: `src/ScamAlert.Core/Broker/RemoteAccessBroker.cs`
- Create: `tests/ScamAlert.Core.Tests/Broker/RemoteAccessBrokerTests.cs`

- [ ] **Step 1: Write failing broker orchestration tests**

Create `tests/ScamAlert.Core.Tests/Broker/RemoteAccessBrokerTests.cs`:

```csharp
using ScamAlert.Contracts;
using ScamAlert.Core.Broker;
using ScamAlert.Core.Configuration;
using ScamAlert.Core.Policy;
using ScamAlert.Core.Rules;
using ScamAlert.Core.Signals;

namespace ScamAlert.Core.Tests.Broker;

public sealed class RemoteAccessBrokerTests
{
    [Fact]
    public async Task HandleAttemptAsyncPromptsUserAndWritesObservedAndDecisionSignals()
    {
        var sink = new RecordingSignalSink();
        var rules = new RecordingRuleStore();
        var settings = new StaticSettingsStore(new ProtectionSettings(TimeoutPolicy.AllowOnTimeout, 10));
        var prompt = new StaticPrompt(new DecisionPromptResponse(TestAttempt.EventId, UserDecisionKind.BlockOnce, false));
        var broker = new RemoteAccessBroker(settings, rules, sink, prompt, new RemoteAccessPolicyEngine());

        var decision = await broker.HandleAttemptAsync(TestAttempt, CancellationToken.None);

        Assert.Equal(DriverDecisionKind.Block, decision.Decision);
        Assert.Equal("userSelected", decision.Reason);
        Assert.Collection(
            sink.Signals,
            first => Assert.IsType<ObservedInboundAttemptSignal>(first),
            second => Assert.IsType<UserDecisionUpdatedSignal>(second));
    }

    [Fact]
    public async Task HandleAttemptAsyncUsesRememberedRuleWithoutPrompt()
    {
        var sink = new RecordingSignalSink();
        var rules = new RecordingRuleStore
        {
            Rule = new RememberedIpRule(TestAttempt.SourceIp, DriverDecisionKind.Block, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        };
        var settings = new StaticSettingsStore(new ProtectionSettings(TimeoutPolicy.AllowOnTimeout, 10));
        var prompt = new StaticPrompt(null);
        var broker = new RemoteAccessBroker(settings, rules, sink, prompt, new RemoteAccessPolicyEngine());

        var decision = await broker.HandleAttemptAsync(TestAttempt, CancellationToken.None);

        Assert.Equal(DriverDecisionKind.Block, decision.Decision);
        Assert.Equal("rememberedIpRule", decision.Reason);
        Assert.False(prompt.WasCalled);
        Assert.Single(sink.Signals);
    }

    [Fact]
    public async Task HandleAttemptAsyncStoresRememberedDecisionWhenUserChecksRemember()
    {
        var sink = new RecordingSignalSink();
        var rules = new RecordingRuleStore();
        var settings = new StaticSettingsStore(new ProtectionSettings(TimeoutPolicy.AllowOnTimeout, 10));
        var prompt = new StaticPrompt(new DecisionPromptResponse(TestAttempt.EventId, UserDecisionKind.AllowOnce, true));
        var broker = new RemoteAccessBroker(settings, rules, sink, prompt, new RemoteAccessPolicyEngine());

        var decision = await broker.HandleAttemptAsync(TestAttempt, CancellationToken.None);

        Assert.Equal(DriverDecisionKind.Allow, decision.Decision);
        Assert.NotNull(rules.Rule);
        Assert.Equal(TestAttempt.SourceIp, rules.Rule.SourceIp);
        Assert.Equal(DriverDecisionKind.Allow, rules.Rule.Decision);
    }

    [Fact]
    public async Task HandleAttemptAsyncAppliesTimeoutPolicyWhenPromptReturnsNoDecision()
    {
        var sink = new RecordingSignalSink();
        var rules = new RecordingRuleStore();
        var settings = new StaticSettingsStore(new ProtectionSettings(TimeoutPolicy.AllowOnTimeout, 10));
        var prompt = new StaticPrompt(null);
        var broker = new RemoteAccessBroker(settings, rules, sink, prompt, new RemoteAccessPolicyEngine());

        var decision = await broker.HandleAttemptAsync(TestAttempt, CancellationToken.None);

        Assert.Equal(DriverDecisionKind.Allow, decision.Decision);
        Assert.Equal("timeoutPolicy", decision.Reason);
    }

    private static readonly ProtectedConnectionAttempt TestAttempt = new(
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        DateTimeOffset.Parse("2026-05-06T12:00:00Z"),
        "203.0.113.10",
        3389,
        ProtectedService.Rdp);

    private sealed class StaticPrompt(DecisionPromptResponse? response) : IConnectionDecisionPrompt
    {
        public bool WasCalled { get; private set; }

        public Task<DecisionPromptResponse?> RequestDecisionAsync(DecisionPromptRequest request, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.FromResult(response);
        }
    }

    private sealed class RecordingSignalSink : ISignalSink
    {
        public List<object> Signals { get; } = [];

        public Task AppendAsync<TSignal>(TSignal signal, CancellationToken cancellationToken)
        {
            Signals.Add(signal!);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingRuleStore : IRememberedRuleStore
    {
        public RememberedIpRule? Rule { get; set; }

        public Task<RememberedIpRule?> FindBySourceIpAsync(string sourceIp, CancellationToken cancellationToken)
        {
            return Task.FromResult(Rule?.SourceIp == sourceIp ? Rule : null);
        }

        public Task UpsertAsync(RememberedIpRule rule, CancellationToken cancellationToken)
        {
            Rule = rule;
            return Task.CompletedTask;
        }
    }

    private sealed class StaticSettingsStore(ProtectionSettings settings) : IProtectionSettingsStore
    {
        public Task<ProtectionSettings> GetAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(settings);
        }

        public Task SaveAsync(ProtectionSettings settings, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests/ScamAlert.Core.Tests/ScamAlert.Core.Tests.csproj --filter RemoteAccessBrokerTests
```

Expected: build fails because broker orchestration types do not exist.

- [ ] **Step 3: Implement broker prompt interface**

Create `src/ScamAlert.Core/Broker/IConnectionDecisionPrompt.cs`:

```csharp
using ScamAlert.Contracts;

namespace ScamAlert.Core.Broker;

public interface IConnectionDecisionPrompt
{
    Task<DecisionPromptResponse?> RequestDecisionAsync(DecisionPromptRequest request, CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Implement broker orchestration**

Create `src/ScamAlert.Core/Broker/RemoteAccessBroker.cs`:

```csharp
using ScamAlert.Contracts;
using ScamAlert.Core.Configuration;
using ScamAlert.Core.Policy;
using ScamAlert.Core.Rules;
using ScamAlert.Core.Signals;

namespace ScamAlert.Core.Broker;

public sealed class RemoteAccessBroker(
    IProtectionSettingsStore settingsStore,
    IRememberedRuleStore rememberedRules,
    ISignalSink signalSink,
    IConnectionDecisionPrompt prompt,
    RemoteAccessPolicyEngine policyEngine)
{
    public async Task<DriverDecisionResponse> HandleAttemptAsync(
        ProtectedConnectionAttempt attempt,
        CancellationToken cancellationToken)
    {
        var settings = await settingsStore.GetAsync(cancellationToken);
        var observed = new ObservedInboundAttemptSignal(
            attempt.EventId,
            attempt.OccurredAt,
            attempt.SourceIp,
            attempt.DestinationPort,
            attempt.ProtectedService,
            settings.TimeoutPolicy,
            DecisionStatus.Pending);

        await signalSink.AppendAsync(observed, cancellationToken);

        var rememberedRule = await rememberedRules.FindBySourceIpAsync(attempt.SourceIp, cancellationToken);
        var rememberedDecision = policyEngine.EvaluateRememberedRule(attempt, rememberedRule);
        if (rememberedDecision is not null)
        {
            return rememberedDecision;
        }

        var request = new DecisionPromptRequest(
            attempt.EventId,
            attempt.OccurredAt,
            attempt.SourceIp,
            attempt.DestinationPort,
            attempt.ProtectedService,
            settings.TimeoutPolicy,
            settings.PromptTimeoutSeconds);

        var response = await prompt.RequestDecisionAsync(request, cancellationToken);
        if (response is null)
        {
            return policyEngine.ApplyTimeout(attempt, settings.TimeoutPolicy);
        }

        if (response.Remember)
        {
            var rule = policyEngine.BuildRememberedRule(attempt, response.Decision, DateTimeOffset.UtcNow);
            await rememberedRules.UpsertAsync(rule, cancellationToken);
        }

        var userDecision = new UserDecisionUpdatedSignal(
            Guid.NewGuid(),
            attempt.EventId,
            DateTimeOffset.UtcNow,
            attempt.SourceIp,
            response.Decision,
            response.Remember,
            "userSelected");

        await signalSink.AppendAsync(userDecision, cancellationToken);
        return policyEngine.ApplyUserDecision(attempt, response.Decision);
    }
}
```

- [ ] **Step 5: Verify broker orchestration**

Run:

```powershell
dotnet test tests/ScamAlert.Core.Tests/ScamAlert.Core.Tests.csproj --filter RemoteAccessBrokerTests
```

Expected: all `RemoteAccessBrokerTests` pass.

- [ ] **Step 6: Commit broker orchestration**

Run:

```powershell
git add src/ScamAlert.Core tests/ScamAlert.Core.Tests
git commit -m "feat: add remote access broker orchestration"
```

Expected: commit succeeds.

## Task 6: Named-Pipe Driver Simulator Boundary

**Files:**
- Create: `src/ScamAlert.Broker/DriverPipe/NamedPipeDriverServer.cs`
- Modify: `tools/ScamAlert.DriverSimulator/Program.cs`
- Create: `docs/driver/wfp-integration-contract.md`

- [ ] **Step 1: Add driver handoff contract documentation**

Create `docs/driver/wfp-integration-contract.md`:

~~~markdown
# WFP Driver Integration Contract

The broker owns policy. The native driver or its user-mode bridge sends one newline-terminated `ProtectedConnectionAttempt` JSON object over the `scamalert-driver-events` named pipe and waits for one newline-terminated `DriverDecisionResponse` JSON object.

Pipe name: `scamalert-driver-events`

Request JSON:

```json
{"eventId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","occurredAt":"2026-05-06T12:00:00+00:00","sourceIp":"203.0.113.10","destinationPort":3389,"protectedService":"rdp"}
```

Response JSON:

```json
{"observedEventId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","decision":"allow","reason":"userSelected"}
```

The driver must apply the persisted fail behavior if the broker pipe is unavailable. The MVP default fail behavior is allow. The driver must bound pending authorization time and complete the WFP operation with the final allow/block decision.
~~~

- [ ] **Step 2: Implement broker-side driver pipe server**

Create `src/ScamAlert.Broker/DriverPipe/NamedPipeDriverServer.cs`:

```csharp
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScamAlert.Contracts;
using ScamAlert.Core.Broker;

namespace ScamAlert.Broker.DriverPipe;

public sealed class NamedPipeDriverServer(
    RemoteAccessBroker broker,
    ILogger<NamedPipeDriverServer> logger) : BackgroundService
{
    public const string PipeName = "scamalert-driver-events";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(
                PipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            await pipe.WaitForConnectionAsync(stoppingToken);

            try
            {
                using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
                await using var writer = new StreamWriter(pipe, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true)
                {
                    AutoFlush = true
                };

                var requestLine = await reader.ReadLineAsync(stoppingToken);
                if (string.IsNullOrWhiteSpace(requestLine))
                {
                    continue;
                }

                var attempt = JsonSerializer.Deserialize<ProtectedConnectionAttempt>(requestLine, SignalJson.Options);

                if (attempt is null)
                {
                    continue;
                }

                var decision = await broker.HandleAttemptAsync(attempt, stoppingToken);
                var responseLine = JsonSerializer.Serialize(decision, SignalJson.Options);
                await writer.WriteLineAsync(responseLine.AsMemory(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process driver pipe request.");
            }
        }
    }
}
```

- [ ] **Step 3: Implement driver simulator console**

Replace `tools/ScamAlert.DriverSimulator/Program.cs`:

```csharp
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using ScamAlert.Contracts;

var sourceIp = GetArgument("--ip", "203.0.113.10");
var port = int.Parse(GetArgument("--port", "3389"));
if (!ProtectedServiceMap.TryFromPort(port, out var service))
{
    Console.Error.WriteLine($"Port {port} is not protected by ScamAlert.");
    return 2;
}

var attempt = new ProtectedConnectionAttempt(
    Guid.NewGuid(),
    DateTimeOffset.UtcNow,
    sourceIp,
    port,
    service);

await using var pipe = new NamedPipeClientStream(".", "scamalert-driver-events", PipeDirection.InOut, PipeOptions.Asynchronous);
await pipe.ConnectAsync(3000);
using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
await using var writer = new StreamWriter(pipe, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true)
{
    AutoFlush = true
};

await writer.WriteLineAsync(JsonSerializer.Serialize(attempt, SignalJson.Options));

var responseLine = await reader.ReadLineAsync();
var response = responseLine is null
    ? null
    : JsonSerializer.Deserialize<DriverDecisionResponse>(responseLine, SignalJson.Options);
Console.WriteLine(JsonSerializer.Serialize(response, SignalJson.Options));
return 0;

static string GetArgument(string name, string fallback)
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

- [ ] **Step 4: Verify driver simulator build**

Run:

```powershell
dotnet build src/ScamAlert.Broker/ScamAlert.Broker.csproj
dotnet build tools/ScamAlert.DriverSimulator/ScamAlert.DriverSimulator.csproj
```

Expected: both projects build successfully.

- [ ] **Step 5: Commit driver simulator boundary**

Run:

```powershell
git add src/ScamAlert.Broker tools/ScamAlert.DriverSimulator docs/driver
git commit -m "feat: add driver simulator boundary"
```

Expected: commit succeeds.

## Task 7: Tray Prompt IPC And UI

**Files:**
- Create: `src/ScamAlert.Broker/TrayPrompt/NamedPipeTrayPromptClient.cs`
- Create: `src/ScamAlert.Tray/PromptPipeServer.cs`
- Create: `src/ScamAlert.Tray/ConnectionPromptForm.cs`
- Modify: `src/ScamAlert.Tray/Program.cs`

- [ ] **Step 1: Implement broker prompt client**

Create `src/ScamAlert.Broker/TrayPrompt/NamedPipeTrayPromptClient.cs`:

```csharp
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using ScamAlert.Contracts;
using ScamAlert.Core.Broker;

namespace ScamAlert.Broker.TrayPrompt;

public sealed class NamedPipeTrayPromptClient : IConnectionDecisionPrompt
{
    public const string PipeName = "scamalert-tray-prompts";

    public async Task<DecisionPromptResponse?> RequestDecisionAsync(
        DecisionPromptRequest request,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds));

        try
        {
            await using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(1000, timeout.Token);
            using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            await using var writer = new StreamWriter(pipe, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true)
            {
                AutoFlush = true
            };

            await writer.WriteLineAsync(JsonSerializer.Serialize(request, SignalJson.Options).AsMemory(), timeout.Token);

            var responseLine = await reader.ReadLineAsync(timeout.Token);
            return string.IsNullOrWhiteSpace(responseLine)
                ? null
                : JsonSerializer.Deserialize<DecisionPromptResponse>(responseLine, SignalJson.Options);
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
    }
}
```

- [ ] **Step 2: Implement tray prompt form**

Create `src/ScamAlert.Tray/ConnectionPromptForm.cs`:

```csharp
using ScamAlert.Contracts;

namespace ScamAlert.Tray;

public sealed class ConnectionPromptForm : Form
{
    private readonly CheckBox rememberCheckBox = new() { Text = "Remember this IP for protected remote access", AutoSize = true };
    private DecisionPromptResponse? response;

    public ConnectionPromptForm(DecisionPromptRequest request)
    {
        Text = "ScamAlert";
        Width = 460;
        Height = 220;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var message = new Label
        {
            Text = $"Incoming {request.ProtectedService} connection from {request.SourceIp}:{request.DestinationPort}",
            AutoSize = false,
            Width = 420,
            Height = 48,
            Left = 20,
            Top = 20
        };

        var allow = new Button { Text = "Allow Once", Width = 120, Left = 80, Top = 120 };
        var block = new Button { Text = "Block Once", Width = 120, Left = 240, Top = 120 };
        rememberCheckBox.Left = 20;
        rememberCheckBox.Top = 82;

        allow.Click += (_, _) => Complete(request, UserDecisionKind.AllowOnce);
        block.Click += (_, _) => Complete(request, UserDecisionKind.BlockOnce);

        Controls.Add(message);
        Controls.Add(rememberCheckBox);
        Controls.Add(allow);
        Controls.Add(block);
    }

    public DecisionPromptResponse? PromptResponse => response;

    private void Complete(DecisionPromptRequest request, UserDecisionKind decision)
    {
        response = new DecisionPromptResponse(request.ObservedEventId, decision, rememberCheckBox.Checked);
        DialogResult = DialogResult.OK;
        Close();
    }
}
```

- [ ] **Step 3: Implement tray prompt pipe server**

Create `src/ScamAlert.Tray/PromptPipeServer.cs`:

```csharp
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using ScamAlert.Contracts;

namespace ScamAlert.Tray;

public sealed class PromptPipeServer : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly SynchronizationContext uiContext;

    public PromptPipeServer(SynchronizationContext uiContext)
    {
        this.uiContext = uiContext;
    }

    public void Start()
    {
        _ = Task.Run(RunAsync);
    }

    public void Dispose()
    {
        stop.Cancel();
        stop.Dispose();
    }

    private async Task RunAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(
                "scamalert-tray-prompts",
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            await pipe.WaitForConnectionAsync(stop.Token);
            using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            await using var writer = new StreamWriter(pipe, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true)
            {
                AutoFlush = true
            };

            var requestLine = await reader.ReadLineAsync(stop.Token);
            var request = string.IsNullOrWhiteSpace(requestLine)
                ? null
                : JsonSerializer.Deserialize<DecisionPromptRequest>(requestLine, SignalJson.Options);
            if (request is null)
            {
                continue;
            }

            var completion = new TaskCompletionSource<DecisionPromptResponse?>();
            uiContext.Post(_ =>
            {
                using var form = new ConnectionPromptForm(request);
                form.ShowDialog();
                completion.SetResult(form.PromptResponse);
            }, null);

            var response = await completion.Task.WaitAsync(stop.Token);
            await writer.WriteLineAsync(JsonSerializer.Serialize(response, SignalJson.Options).AsMemory(), stop.Token);
        }
    }
}
```

- [ ] **Step 4: Wire tray app startup**

Replace `src/ScamAlert.Tray/Program.cs`:

```csharp
namespace ScamAlert.Tray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        using var notifyIcon = new NotifyIcon
        {
            Text = "ScamAlert",
            Visible = true,
            Icon = SystemIcons.Shield,
            ContextMenuStrip = new ContextMenuStrip()
        };

        notifyIcon.ContextMenuStrip.Items.Add("Exit", null, (_, _) => Application.Exit());

        var uiContext = SynchronizationContext.Current;
        if (uiContext is null)
        {
            uiContext = new WindowsFormsSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(uiContext);
        }

        using var promptServer = new PromptPipeServer(uiContext);
        promptServer.Start();

        Application.Run();
    }
}
```

- [ ] **Step 5: Verify tray build**

Run:

```powershell
dotnet build src/ScamAlert.Tray/ScamAlert.Tray.csproj
```

Expected: build succeeds.

- [ ] **Step 6: Commit tray prompt UI**

Run:

```powershell
git add src/ScamAlert.Broker src/ScamAlert.Tray
git commit -m "feat: add tray decision prompt"
```

Expected: commit succeeds.

## Task 8: Broker Host Wiring And End-To-End Simulator Verification

**Files:**
- Modify: `src/ScamAlert.Broker/Program.cs`
- Modify: `src/ScamAlert.Broker/appsettings.json`
- Create: `src/ScamAlert.Broker/Configuration/ScamAlertPaths.cs`

- [ ] **Step 1: Add broker paths helper**

Create `src/ScamAlert.Broker/Configuration/ScamAlertPaths.cs`:

```csharp
namespace ScamAlert.Broker.Configuration;

public static class ScamAlertPaths
{
    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScamAlert");

    public static string SignalFile => Path.Combine(DataDirectory, "signals.jsonl");

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public static string RulesFile => Path.Combine(DataDirectory, "remembered-rules.json");
}
```

- [ ] **Step 2: Wire broker dependencies**

Replace `src/ScamAlert.Broker/Program.cs`:

```csharp
using ScamAlert.Broker.Configuration;
using ScamAlert.Broker.DriverPipe;
using ScamAlert.Broker.TrayPrompt;
using ScamAlert.Core.Broker;
using ScamAlert.Core.Configuration;
using ScamAlert.Core.Policy;
using ScamAlert.Core.Rules;
using ScamAlert.Core.Signals;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<RemoteAccessPolicyEngine>();
builder.Services.AddSingleton<IProtectionSettingsStore>(_ => new FileProtectionSettingsStore(ScamAlertPaths.SettingsFile));
builder.Services.AddSingleton<IRememberedRuleStore>(_ => new FileRememberedRuleStore(ScamAlertPaths.RulesFile));
builder.Services.AddSingleton<ISignalSink>(_ => new JsonlSignalSink(ScamAlertPaths.SignalFile));
builder.Services.AddSingleton<IConnectionDecisionPrompt, NamedPipeTrayPromptClient>();
builder.Services.AddSingleton<RemoteAccessBroker>();
builder.Services.AddHostedService<NamedPipeDriverServer>();

var host = builder.Build();
await host.RunAsync();
```

Replace `src/ScamAlert.Broker/appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  }
}
```

- [ ] **Step 3: Verify full solution build and tests**

Run:

```powershell
dotnet build ScamAlert.sln
dotnet test tests/ScamAlert.Core.Tests/ScamAlert.Core.Tests.csproj
```

Expected: build succeeds and all tests pass.

- [ ] **Step 4: Manually verify allow-on-timeout without tray**

Start broker:

```powershell
dotnet run --project src/ScamAlert.Broker/ScamAlert.Broker.csproj
```

In another terminal, run simulator:

```powershell
dotnet run --project tools/ScamAlert.DriverSimulator/ScamAlert.DriverSimulator.csproj -- --ip 203.0.113.10 --port 3389
```

Expected simulator output contains:

```json
{"observedEventId":"<guid>","decision":"allow","reason":"timeoutPolicy"}
```

Expected signal file exists:

```powershell
Get-Content "$env:LOCALAPPDATA\ScamAlert\signals.jsonl"
```

Expected: at least one line contains `"eventType":"ObservedInboundAttempt"` and `"sourceIp":"203.0.113.10"`.

- [ ] **Step 5: Manually verify tray decision path**

Start broker:

```powershell
dotnet run --project src/ScamAlert.Broker/ScamAlert.Broker.csproj
```

Start tray:

```powershell
dotnet run --project src/ScamAlert.Tray/ScamAlert.Tray.csproj
```

Run simulator:

```powershell
dotnet run --project tools/ScamAlert.DriverSimulator/ScamAlert.DriverSimulator.csproj -- --ip 198.51.100.25 --port 22
```

Click `Block Once` in the tray prompt.

Expected simulator output contains:

```json
{"observedEventId":"<guid>","decision":"block","reason":"userSelected"}
```

Expected signal file contains one `ObservedInboundAttempt` line and one `UserDecisionUpdated` line for `198.51.100.25`.

- [ ] **Step 6: Commit broker host wiring**

Run:

```powershell
git add src/ScamAlert.Broker src/ScamAlert.Tray tools/ScamAlert.DriverSimulator
git commit -m "feat: wire broker host and simulator verification"
```

Expected: commit succeeds.

## Task 9: Final Verification And Notes

**Files:**
- Modify: `docs/superpowers/plans/2026-05-06-scamalert-local-mvp-foundation.md` only if execution discovers a required command correction.

- [ ] **Step 1: Run complete automated verification**

Run:

```powershell
dotnet build ScamAlert.sln
dotnet test tests/ScamAlert.Core.Tests/ScamAlert.Core.Tests.csproj
```

Expected: solution build succeeds and all tests pass.

- [ ] **Step 2: Inspect git status**

Run:

```powershell
git status --short
```

Expected: no uncommitted source changes remain after the final commit.

- [ ] **Step 3: Record driver follow-up boundary for next plan**

Confirm `docs/driver/wfp-integration-contract.md` states:

```text
Pipe name: scamalert-driver-events
Request: ProtectedConnectionAttempt JSON
Response: DriverDecisionResponse JSON
Broker-unavailable behavior: persisted allow/block timeout policy
Protected ports: 3389, 22, 23
```

Expected: all four facts are present.
