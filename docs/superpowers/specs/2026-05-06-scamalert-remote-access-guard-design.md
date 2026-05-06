# ScamAlert Remote Access Guard MVP Design

Date: 2026-05-06

## Purpose

ScamAlert is a local Windows protection app that alerts users when remote-access traffic may indicate they are being scammed. The MVP focuses strictly on inbound RDP, SSH, and Telnet protection. Broader behavior detection, mobile support, and cloud reputation services are intentionally deferred.

The product should be designed as a local protection platform, not as a single-purpose RDP blocker. The first module is remote-access connection protection, but the core service, policy model, signal sink, and UI decision flow should be reusable for future modules such as suspicious screen behavior, remote-control software activity, unexpected software installation, credential-store access, and persistence changes.

## MVP Scope

The MVP protects inbound TCP attempts for:

- RDP on port 3389
- SSH on port 22
- Telnet on port 23

For every observed inbound attempt, ScamAlert records a local signal. When the user makes a decision, ScamAlert records a follow-up signal. Cloud reporting is represented by a local file-writing stub so the local protection loop can be tested before any server API is built.

## Non-Goals

- No cloud API implementation.
- No mobile implementation.
- No general EDR or antivirus behavior blocking.
- No outbound traffic inspection.
- No attempt to classify all remote-control tools in the MVP.
- No cloud dependency for local allow/block decisions.

## Architecture

### Components

#### Remote Access WFP Driver

A small C++ Windows Filtering Platform driver monitors inbound TCP authorization for the protected ports. Its responsibilities are intentionally narrow:

- Observe inbound TCP attempts to ports 3389, 22, and 23.
- Send connection-attempt events to the local broker service.
- Pend connection authorization only when the broker policy requires a decision.
- Complete authorization with allow or block after the broker responds.
- Apply the configured timeout behavior when the broker or UI does not respond.

The driver must not perform cloud calls, DNS/IP reputation lookups, UI work, or high-level scam classification.

#### Protection Broker Service

A Windows service owns ScamAlert's local policy and product behavior. It should likely be implemented in .NET for easier persistence, configuration, IPC, web requests, and future application logic.

The broker service is responsible for:

- Receiving driver events.
- Evaluating local policy and remembered rules.
- Sending decision requests to the tray UI.
- Returning allow/block decisions to the driver.
- Persisting configuration.
- Appending observed-attempt and user-decision signals to a local JSONL file.
- Providing a replaceable boundary for future cloud sync.

#### Tray UI

The tray UI displays clear prompts when an unknown inbound protected connection needs user input.

The default interaction is one-time:

- `Allow Once`
- `Block Once`

The prompt also includes an explicit persistence option:

- `Remember this IP for protected remote access`

When selected, the remembered decision applies to the source IP across all currently protected remote-access services: RDP, SSH, and Telnet.

#### Signal Sink Stub

The MVP signal sink appends JSONL records to a local file. This is a stand-in for future cloud reporting.

The sink records:

- Every observed inbound protected attempt.
- A follow-up update when the user makes a decision.
- Local policy context such as timeout mode and whether a remembered rule was used.

The sink should be defined behind an interface so a future cloud uploader can be added without changing the driver or tray UI.

## Policy Model

### Install-Time Configuration

During installation, the user is asked how unknown inbound protected connections should behave when the user does not respond in time.

Options:

- `Allow and alert` - MVP default.
- `Block unless approved`.

This setting must remain configurable after installation.

### Runtime Decisions

Unknown inbound attempts follow this flow:

1. The driver observes the inbound connection.
2. The broker records an `ObservedInboundAttempt` signal.
3. The broker checks remembered local rules for the source IP.
4. If a remembered rule exists, the broker returns that decision.
5. If no remembered rule exists, the broker asks the tray UI.
6. If the user chooses `Allow Once`, only the current attempt is allowed.
7. If the user chooses `Block Once`, only the current attempt is blocked.
8. If the remember option is selected, the decision is stored for that source IP across protected remote-access services.
9. If the user does not respond before timeout, the broker applies the configured timeout policy.

The MVP default timeout policy is allow.

### Remembered Rule Scope

Remembered rules are keyed by source IP and apply across all currently protected remote-access services. For example, a remembered block for `203.0.113.10` applies to RDP, SSH, and Telnet attempts from that IP.

The rule model should preserve service context in event history even though the default remembered rule scope is IP-wide.

## Local Signal Format

Signals are written as JSONL. Each line is one event.

Example observed attempt:

```json
{
  "eventType": "ObservedInboundAttempt",
  "eventId": "uuid",
  "occurredAt": "2026-05-06T12:00:00Z",
  "sourceIp": "203.0.113.10",
  "destinationPort": 3389,
  "protectedService": "rdp",
  "localPolicyMode": "allowOnTimeout",
  "decisionStatus": "pending"
}
```

Example user decision update:

```json
{
  "eventType": "UserDecisionUpdated",
  "eventId": "decision-event-uuid",
  "observedEventId": "observed-attempt-event-uuid",
  "occurredAt": "2026-05-06T12:00:05Z",
  "sourceIp": "203.0.113.10",
  "decision": "blockOnce",
  "remembered": false,
  "reason": "userSelected"
}
```

The local signal schema should support future protection modules by using a common event envelope and module-specific details.

## Future Expansion Boundary

Future ScamAlert modules may monitor or enforce behavior beyond inbound network access, including:

- Screen blackout or monitor-darkening behavior during a remote session.
- Suspicious remote-control software execution.
- Unexpected software installation.
- Keylogging indicators.
- Browser credential-store access.
- Startup persistence changes.

To avoid coding into a corner, future modules should emit common `ProtectionEvent` records and ask the broker for policy decisions through a shared interface. Low-level modules collect and enforce. The broker owns policy. The tray UI owns user interaction. Signal sinks own reporting.

## Error Handling

The system must handle failures conservatively and explicitly:

- If the tray UI is unavailable, apply the configured timeout policy.
- If the broker service is unavailable, the driver applies the persisted fail behavior that corresponds to the user's configured allow-on-timeout or block-on-timeout policy.
- If the signal file cannot be written, local enforcement should continue and the broker should log the sink failure.
- If cloud sync is later unavailable, local protection continues and queued signals can be retried.
- Driver timeouts must be bounded to avoid hanging inbound connection authorization indefinitely.

## Testing Strategy

MVP validation should focus on local behavior before cloud work:

- Unit-test broker policy decisions for default allow, default block, one-time decisions, remembered IP rules, and timeout behavior.
- Unit-test signal JSONL serialization for observed attempts and decision updates.
- Integration-test broker-to-tray decision flow with a fake driver event source.
- Integration-test broker-to-driver decision flow with a user-mode driver simulator before testing the real WFP driver.
- Manually validate inbound RDP, SSH, and Telnet attempts from known test machines.
- Validate service-down and UI-down behavior against both timeout policies.

## Open Implementation Notes

- The exact driver IPC mechanism should be selected during implementation planning.
- The local persistence mechanism for settings and remembered rules can start simple, but should be replaceable.
- Driver signing and installer flow will need dedicated planning before distribution.
- The MVP should avoid promising full scam prevention; it provides focused inbound remote-access protection and local decision logging.
