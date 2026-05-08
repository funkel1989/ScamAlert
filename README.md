# ScamAlert

ScamAlert is a Windows remote-access protection prototype. The current MVP watches simulated inbound RDP, SSH, and Telnet attempts, asks the user for a decision through a tray UI, records local JSONL signals, and stores remembered IP decisions.

The real WFP driver monitor is not wired into the app yet. For now, use `ScamAlert.DriverSimulator` to exercise the broker/tray flow.

## Current Feature Status

- [x] Broker + tray named-pipe prompt flow for simulated inbound attempts.
- [x] Local JSONL signal writing and remembered allow/block IP rules.
- [x] API endpoints for customer creation and alert raising.
- [x] EF Core SQLite persistence for customers, subscriptions, contacts, devices, alerts, and notification attempts.
- [x] Twilio-ready SMS notification gateway with DB-backed acknowledgment tokens and inbound acknowledgment webhook.
- [x] Background escalation worker (time-based primary-to-secondary escalation after no response).
- [x] Broker cloud alert uplink (optional): deduped enqueue, durable outbox, HTTP retries, dead-letter on permanent failures.
- [ ] Twilio voice-call workflow and richer retry policy.
- [ ] WFP production monitor integration.

## Projects

- `src/ScamAlert.Broker` - background broker that receives driver events, applies local policy, talks to the tray UI, and writes local signals.
- `src/ScamAlert.Tray` - Windows tray app and decision prompt UI.
- `src/ScamAlert.Api` - ASP.NET Core API host for controller-based endpoints.
- `src/ScamAlert.Data` - EF Core data layer (SQLite) for customers, subscriptions, contacts, devices, alerts, and notification attempts.
- `tools/ScamAlert.DriverSimulator` - command-line simulator for inbound protected connection attempts.
- `src/ScamAlert.Contracts` - shared contract types and JSON settings.
- `src/ScamAlert.Core` - policy, remembered rules, settings, and signal writing.
- `tests/ScamAlert.Core.Tests` - unit tests for policy, persistence, named-pipe protocol, signal contracts, and cloud outbox helpers.
- `tests/ScamAlert.Api.Tests` - API integration tests (WebApplicationFactory) for alert idempotency and escalation.

## Prerequisites

- Windows
- .NET 10 SDK
- Visual Studio 2022, optional but useful for running broker and tray together

## Build And Test

From the repo root:

```powershell
dotnet restore ScamAlert.sln
dotnet build ScamAlert.sln
dotnet test tests/ScamAlert.Core.Tests/ScamAlert.Core.Tests.csproj
dotnet test tests/ScamAlert.Api.Tests/ScamAlert.Api.Tests.csproj
```

## Launch With Visual Studio

Open `ScamAlert.sln`.

Use the solution launch profile named `StartUp` if Visual Studio shows it. That profile starts both:

- `ScamAlert.Broker`
- `ScamAlert.Tray`

If Visual Studio asks for a startup project instead, configure multiple startup projects and set both `src\ScamAlert.Broker\ScamAlert.Broker.csproj` and `src\ScamAlert.Tray\ScamAlert.Tray.csproj` to `Start`.

The tray app shows a shield icon in the Windows system tray. It may be hidden in the tray overflow menu.

## Launch From The Terminal

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

## API Quick Start

Run the API:

```powershell
dotnet run --project src/ScamAlert.Api/ScamAlert.Api.csproj
```

Create a customer with contacts/devices:

```powershell
curl -X POST "http://localhost:5000/api/customers" `
  -H "Content-Type: application/json" `
  -d "{\"name\":\"Contoso\",\"email\":\"owner@contoso.com\",\"planCode\":\"pro\",\"contacts\":[{\"fullName\":\"Primary Admin\",\"phoneNumber\":\"+15555550100\",\"escalationOrder\":1},{\"fullName\":\"Secondary Admin\",\"phoneNumber\":\"+15555550101\",\"escalationOrder\":2}],\"devices\":[{\"deviceName\":\"Reception PC\",\"externalDeviceId\":\"device-001\"}]}"
```

Raise an alert and simulate acknowledgment at escalation step 2:

```powershell
curl -X POST "http://localhost:5000/api/alerts" `
  -H "Content-Type: application/json" `
  -d "{\"externalDeviceId\":\"device-001\",\"sourceIp\":\"203.0.113.10\",\"destinationPort\":3389,\"service\":\"rdp\",\"simulateAcknowledgeAtEscalationOrder\":2,\"clientEventId\":null}"
```

Include a stable `clientEventId` (for example the broker attempt `EventId`) when callers may retry the same logical alert; duplicate posts with the same `clientEventId` for the same device return the existing alert without sending notifications again.

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
- Simulator returns `timeoutPolicy`: the broker did not get a valid tray decision before the prompt timeout.
- Remembered decision keeps applying: delete `%LOCALAPPDATA%\ScamAlert\remembered-rules.json`.
- No signal file exists: run at least one protected simulator command while the broker is running.

## Actual Driver Monitor

The planned production monitor is a Windows Filtering Platform driver plus a user-mode bridge. The implementation plan is in `docs/superpowers/plans/2026-05-06-scamalert-wfp-monitor.md`.

Driver work needs a Windows test VM with the Windows Driver Kit installed. The current local MVP does not require the WDK.
