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

The driver must apply the persisted fail behavior if the broker pipe is unavailable. The MVP default fail behavior is allow. The driver must bound pending WFP authorization time and complete the WFP operation with the final allow/block decision.
