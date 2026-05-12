# ScamAlert

ScamAlert is a Windows remote-access protection prototype. The normal local MVP watches simulated inbound RDP, SSH, and Telnet attempts, asks the user for a decision through a tray UI, records local JSONL signals, and stores remembered IP decisions.

This branch also includes the native Windows Filtering Platform (WFP) monitor and `ScamAlert.DriverBridge` path for dev-VM testing. Use `ScamAlert.DriverSimulator` for the fastest broker/tray workflow; use the WFP driver path only inside a test-signing Windows VM.

## Current Feature Status

- [x] Broker + tray named-pipe prompt flow for simulated inbound attempts.
- [x] Native WFP driver + DriverBridge dev path for RDP/SSH/Telnet attempts in a test VM.
- [x] Local JSONL signal writing and remembered allow/block IP rules.
- [x] API endpoints for customer creation and alert raising.
- [x] EF Core SQL Server persistence for customers, subscriptions, contacts, devices, alerts, and notification attempts.
- [x] Twilio-ready SMS notification gateway with DB-backed acknowledgment tokens and inbound acknowledgment webhook.
- [x] Background escalation worker (time-based primary-to-secondary escalation after no response).
- [x] Broker cloud alert uplink (optional): deduped enqueue, durable outbox, HTTP retries, dead-letter on permanent failures.
- [ ] Twilio voice-call workflow and richer retry policy.
- [ ] Signed/packaged production WFP monitor installer.

## Projects

- `src/ScamAlert.Broker` - background broker that receives driver events, applies local policy, talks to the tray UI, and writes local signals.
- `src/ScamAlert.Broker.Client` - reusable named-pipe client used by the simulator and DriverBridge.
- `src/ScamAlert.Tray` - Windows tray app and decision prompt UI.
- `src/ScamAlert.DriverBridge` - user-mode worker that reads events from `\\.\ScamAlertWfp`, asks the broker for a decision, and completes the kernel event.
- `src/ScamAlert.Api` - ASP.NET Core API host for controller-based endpoints.
- `src/ScamAlert.AppHost` - .NET Aspire orchestrator (runs API + dashboard for local dev).
- `src/ScamAlert.ServiceDefaults` - shared Aspire service defaults (OpenTelemetry, health, resilience) referenced by the API.
- `src/ScamAlert.Data` - EF Core data layer (SQL Server) for customers, subscriptions, contacts, devices, alerts, and notification attempts.
- `native/ScamAlert.WfpDriver` - native WFP callout driver for protected inbound ports.
- `native/ScamAlert.Driver.Shared` - C-compatible IOCTL contract shared by the driver and managed bridge.
- `scripts/driver` - host/VM setup, build, deploy, traffic prep, and diagnostic scripts for the WFP path.
- `tools/ScamAlert.DriverSimulator` - command-line simulator for inbound protected connection attempts.
- `src/ScamAlert.Contracts` - shared contract types and JSON settings.
- `src/ScamAlert.Core` - policy, remembered rules, settings, and signal writing.
- `tests/ScamAlert.Core.Tests` - unit tests for policy, persistence, named-pipe protocol, signal contracts, and cloud outbox helpers.
- `tests/ScamAlert.Api.Tests` - API integration tests (WebApplicationFactory) for alert idempotency and escalation.

## Prerequisites

- Windows
- .NET 10 SDK
- SQL Server LocalDB (included with Visual Studio) for running the API and integration tests outside Aspire, or use Aspire AppHost which runs SQL Server in a container
- Visual Studio 2022, optional but useful for running broker and tray together
- Optional for native driver work: Visual Studio C++ tooling, Windows Driver Kit, Hyper-V, and a Windows test-signing VM. Do not enable test-signing on your daily workstation.

## Build And Test

From the repo root:

```powershell
dotnet restore ScamAlert.sln
dotnet build ScamAlert.sln
dotnet test ScamAlert.sln
```

Optional native driver build from the repo root:

```powershell
scripts/driver/check-driver-prereqs.ps1
scripts/driver/build-driver.ps1 -SkipRestore
```

## Launch With Visual Studio

Open `ScamAlert.sln`.

Use the solution launch profile named `StartUp` if Visual Studio shows it. That profile starts both:

- `ScamAlert.Broker`
- `ScamAlert.Tray`

If Visual Studio asks for a startup project instead, configure multiple startup projects and set both `src\ScamAlert.Broker\ScamAlert.Broker.csproj` and `src\ScamAlert.Tray\ScamAlert.Tray.csproj` to `Start`.

The tray app shows a shield icon in the Windows system tray. It may be hidden in the tray overflow menu.

## Launch With Aspire (backend API)

One-time template install (if `aspire-apphost` is not already on your machine):

```powershell
dotnet new install Aspire.ProjectTemplates
```

Run the AppHost (opens the Aspire dashboard and starts `ScamAlert.Api`):

```powershell
dotnet run --project src/ScamAlert.AppHost/ScamAlert.AppHost.csproj
```

The AppHost injects `ConnectionStrings__ScamAlertDb` for the SQL Server database resource (`ScamAlertDb` in `AppHost.cs`). When you run the API alone, `appsettings.json` defaults to LocalDB (`ScamAlert` database on `(localdb)\mssqllocaldb`).

EF Core migrations run automatically when the API starts (`Database.Migrate()`).

## Launch The Simulator Path

Open two or three PowerShell windows from the repo root.

Window 1:

```powershell
dotnet run --project src/ScamAlert.Broker/ScamAlert.Broker.csproj
```

Window 2:

```powershell
dotnet run --project src/ScamAlert.Tray/ScamAlert.Tray.csproj
```

Optional Window 3 (API host):

```powershell
dotnet run --project src/ScamAlert.Api/ScamAlert.Api.csproj
```

Leave broker/tray running while testing. Run the API host when developing controller endpoints.

This path does not load the native driver and does not require the WDK.

## API Quick Start

Run the API:

```powershell
dotnet run --project src/ScamAlert.Api/ScamAlert.Api.csproj
```

Get a bearer token (development bootstrap user shown):

```powershell
curl -X POST "http://localhost:5000/api/auth/token" `
  -H "Content-Type: application/json" `
  -d "{\"username\":\"operator\",\"password\":\"dev-password\"}"
```

Production auth notes:

- Do not store real credentials in appsettings.
- Set `Authentication:BootstrapAdmin:Enabled=true` only for initial bootstrap, then disable it.
- Supply a high-entropy `Authentication:Jwt:SigningKey` (at least 32 bytes).
- Keep `Authentication:Jwt:RequireHttpsMetadata=true` in production.

Use the returned token in authenticated API calls:

```powershell
$token = "<paste-access-token>"
```

Create a customer with contacts/devices:

```powershell
curl -X POST "http://localhost:5000/api/customers" `
  -H "Authorization: Bearer $token" `
  -H "Content-Type: application/json" `
  -d "{\"name\":\"Contoso\",\"email\":\"owner@contoso.com\",\"planCode\":\"pro\",\"contacts\":[{\"fullName\":\"Primary Admin\",\"phoneNumber\":\"+15555550100\",\"escalationOrder\":1},{\"fullName\":\"Secondary Admin\",\"phoneNumber\":\"+15555550101\",\"escalationOrder\":2}],\"devices\":[{\"deviceName\":\"Reception PC\",\"externalDeviceId\":\"device-001\"}]}"
```

Raise an alert and simulate acknowledgment at escalation step 2:

```powershell
curl -X POST "http://localhost:5000/api/alerts" `
  -H "Authorization: Bearer $token" `
  -H "Content-Type: application/json" `
  -d "{\"externalDeviceId\":\"device-001\",\"sourceIp\":\"203.0.113.10\",\"destinationPort\":3389,\"service\":\"rdp\",\"destinationIp\":null,\"transport\":\"tcp\",\"direction\":\"inbound\",\"observedBy\":\"broker\",\"ruleApplied\":null,\"decisionReason\":null,\"notes\":null,\"simulateAcknowledgeAtEscalationOrder\":2,\"clientEventId\":null}"
```

Local broker ingest can call the same endpoint without bearer JWT by sending the provisioned device key header:

```text
X-ScamAlert-DeviceKey: <device-ingest-api-key>
```

Include a stable `clientEventId` (for example the broker attempt `EventId`) when callers may retry the same logical alert; duplicate posts with the same `clientEventId` for the same device return the existing alert without sending notifications again.

List and filter alerts:

```powershell
curl "http://localhost:5000/api/alerts?status=Pending&page=1&pageSize=25" `
  -H "Authorization: Bearer $token"
```

Recent connections for a device:

```powershell
curl "http://localhost:5000/api/devices/<device-guid>/recent-connections?take=50" `
  -H "Authorization: Bearer $token"
```

## Twilio SMS Configuration

The API can send real SMS notifications through Twilio when credentials are configured. If Twilio settings are missing, the API falls back to logging-only notifications.

Set these values in `src/ScamAlert.Api/appsettings.json` or environment variables:

- `Twilio:AccountSid`
- `Twilio:AuthToken`
- `Twilio:FromPhoneNumber`
- `Twilio:StatusCallbackBaseUrl` (public URL that Twilio can reach, for example an ngrok URL)

Webhook endpoints used by Twilio:

- `POST /api/webhooks/twilio/status` - delivery/failure callback updates `NotificationAttempt`.
- `POST /api/webhooks/twilio/inbound-sms` - parses replies like `ACK ABC123` and marks the alert acknowledged.

Webhook security:

- `Twilio:ValidateWebhookSignatures` should remain `true` in production.
- `Twilio:WebhookPublicBaseUrl` should match the public base URL Twilio calls (for signature verification).

Current acknowledgment flow:

1. `POST /api/alerts` creates an alert and notification attempts.
2. Each SMS contains a unique `ACK <token>` instruction.
3. Contact replies by SMS with the token.
4. Inbound webhook resolves the attempt as acknowledged and sets alert status to `Acknowledged`.

## Simulate An Inbound Attempt

Open a third PowerShell window from the repo root:

```powershell
dotnet run --project tools/ScamAlert.DriverSimulator/ScamAlert.DriverSimulator.csproj -- --ip 203.0.113.10 --port 3389
```

Protected ports:

- `3389` - RDP
- `22` - SSH
- `23` - Telnet

Expected behavior:

1. The simulator sends one inbound attempt to the broker.
2. The broker writes an `ObservedInboundAttempt` signal.
3. The tray prompt appears.
4. Choose allow or block.
5. The simulator prints a JSON decision response.
6. The broker writes a `UserDecisionUpdated` signal.

Example response:

```json
{"observedEventId":"4a204790-24c5-4d84-8619-7c8dd316f69a","decision":"allow","reason":"userSelected"}
```

If the tray is not running or the prompt times out, the MVP defaults to allow unless local settings say otherwise.

## Run The WFP Driver Path In A Dev VM

Use this path only for native driver validation. The host builds the driver; the VM runs it with test-signing enabled.

One-time setup:

```powershell
scripts/driver/check-driver-prereqs.ps1
scripts/driver/create-dev-vm.ps1 -IsoPath "D:\HyperV\ISOs\Win11_25H2_EnglishInternational_x64.iso"
scripts/driver/finalize-dev-vm.ps1
```

Then copy `scripts/driver/configure-dev-vm-inside.ps1` into the VM, run it from an elevated PowerShell inside the VM, and reboot when prompted. If PowerShell Direct is available, the copy step can be run from the host:

```powershell
$cred = Get-Credential -UserName dev
$session = New-PSSession -VMName ScamAlertDev -Credential $cred
Copy-Item -ToSession $session -Path scripts/driver/configure-dev-vm-inside.ps1 -Destination C:\Users\dev\Desktop\configure-dev-vm-inside.ps1
Remove-PSSession $session
```

After the VM is configured, verify host-to-VM access:

```powershell
scripts/driver/verify-vm-access.ps1
```

Build and deploy the driver/user-mode pieces from the host:

```powershell
scripts/driver/build-driver.ps1 -SkipRestore
scripts/driver/deploy-driver-to-vm.ps1
scripts/driver/prep-vm-for-traffic.ps1
scripts/driver/deploy-userland-to-vm.ps1
scripts/driver/deploy-tray-to-vm.ps1
```

Log in to the VM interactively so the tray app can run in the user session. Then generate traffic to the VM from the host or another machine:

```powershell
Test-NetConnection -ComputerName <vm-ip> -Port 3389
```

Protected ports are the same as the simulator path: `3389` for RDP, `22` for SSH, and `23` for Telnet.

The WFP flow is:

1. The native driver observes the inbound protected-port attempt.
2. The driver queues a bounded kernel event and pends the WFP operation.
3. `ScamAlert.DriverBridge` reads the event from `\\.\ScamAlertWfp`.
4. DriverBridge sends the attempt to `ScamAlert.Broker` over `scamalert-driver-events`.
5. The broker applies remembered rules or prompts through the tray.
6. DriverBridge posts the allow/block decision back to the driver.
7. The driver completes the pending WFP operation.

Driver safety notes:

- The control device is restricted to LocalSystem and Administrators.
- Kernel event and pending-operation queues are bounded to protect nonpaged pool under traffic bursts.
- The kernel timeout fail-blocks stale pending operations after 60 seconds.
- DriverBridge allows by default if the broker pipe is unavailable, matching the current MVP fail policy.

Driver diagnostics:

```powershell
$cred = Get-Credential -UserName dev
Invoke-Command -VMName ScamAlertDev -Credential $cred -FilePath scripts/driver/probe-driver-stats.ps1
```

`probe-driver-stats.ps1` reports counters such as `ClassifyEntered`, `SelfInjectedSkipped`, `EventsQueued`, `PendOk`, `AllowInjected`, `BlockReleased`, `TimedOutFailBlock`, `EventsDropped`, and `PendingRejected`. `probe-driver-events.ps1` drains raw driver events and does not complete decisions, so use it only when the bridge is stopped or when intentionally inspecting low-level driver output.

Detailed driver docs:

- [Dev environment setup](docs/driver/dev-environment-setup.md) - host/VM setup, deployment, traffic test, and troubleshooting runbook.
- [WDK setup](docs/driver/wdk-setup.md) - host tooling and VM test-signing requirements.
- [WFP integration contract](docs/driver/wfp-integration-contract.md) - binary IOCTL and named-pipe broker contracts.
- [WFP driver current state](docs/driver/wfp-driver-build-plan.md) - current implementation notes, verification checklist, and future work.

## Local Data Files

Runtime data is stored under:

```text
%LOCALAPPDATA%\ScamAlert
```

Files:

- `signals.jsonl` - observed attempts and user decision updates.
- `settings.json` - local timeout behavior.
- `remembered-rules.json` - remembered IP allow/block rules.
- `cloud-alert-dedupe.json` - last cloud alert enqueue time per `(device, source IP, port)` for dedupe windowing (when cloud uplink is enabled).
- `cloud-alerts-pending.jsonl` - pending outbound alert deliveries to the API.
- `cloud-alerts-deadletter.jsonl` - deliveries that exceeded retries or failed with permanent HTTP errors.

### Broker cloud uplink (`CloudAlerts`)

Configure in `src/ScamAlert.Broker/appsettings.json` (or environment variables) to POST deduped `ObservedInboundAttempt` events to `POST /api/alerts`:

- `CloudAlerts:Enabled` - set `true` to enqueue and deliver.
- `CloudAlerts:BaseUrl` - API root (for example `http://localhost:5000`).
- `CloudAlerts:ExternalDeviceId` - must match a device `externalDeviceId` registered for an active customer subscription.
- `CloudAlerts:DeviceIngestApiKey` - API key generated when the device is provisioned (returned by `POST /api/customers`).
- `CloudAlerts:DedupeWindowSeconds` - suppress duplicate enqueues for the same `(ExternalDeviceId, source IP, destination port)` within this window.
- `CloudAlerts:MaxDeliveryAttempts`, `InitialRetryDelaySeconds`, `MaxRetryDelaySeconds`, `PollIntervalSeconds` - outbound retry and polling behavior.

The broker sends `clientEventId` equal to the observed attempt `EventId` so the API can treat retries as idempotent.

### API alert escalation (`Alerts`)

Configure in `src/ScamAlert.Api/appsettings.json`:

- `Alerts:EscalationDelaySeconds` - wait time after a no-response notification before notifying the next escalation tier.
- `Alerts:EscalationPollIntervalSeconds` - how often the background worker scans for due escalations.

Inspect recent signals:

```powershell
Get-Content "$env:LOCALAPPDATA\ScamAlert\signals.jsonl" -Tail 20
```

Reset remembered decisions:

```powershell
Remove-Item "$env:LOCALAPPDATA\ScamAlert\remembered-rules.json" -ErrorAction SilentlyContinue
```

Configure timeout behavior:

```powershell
New-Item -ItemType Directory "$env:LOCALAPPDATA\ScamAlert" -Force | Out-Null
@'
{"timeoutPolicy":"AllowOnTimeout","promptTimeoutSeconds":10}
'@ | Set-Content "$env:LOCALAPPDATA\ScamAlert\settings.json"
```

Use `BlockOnTimeout` instead of `AllowOnTimeout` to make unanswered prompts block in the local policy engine.

## Troubleshooting

- No prompt appears: confirm both `ScamAlert.Broker` and `ScamAlert.Tray` are running.
- Simulator says the broker pipe is unavailable: start `ScamAlert.Broker`.
- DriverBridge says the driver device is unavailable: confirm the `ScamAlertWfp` driver service is running inside the VM with `sc query ScamAlertWfp`.
- `CreateFile('\\.\ScamAlertWfp')` fails with access denied: run the diagnostic or bridge process elevated; the driver device is restricted to Administrators and LocalSystem.
- Driver stats show `EventsDropped` or `PendingRejected`: the bounded kernel queues are protecting the driver under burst traffic; check whether DriverBridge and Broker are running and keeping up.
- Simulator returns `timeoutPolicy`: the broker did not get a valid tray decision before the prompt timeout.
- Remembered decision keeps applying: delete `%LOCALAPPDATA%\ScamAlert\remembered-rules.json`.
- No signal file exists: run at least one protected simulator command while the broker is running.
