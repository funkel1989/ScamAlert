# WFP Driver Integration Contract

The broker owns policy. The native driver or its user-mode bridge sends one newline-terminated `ProtectedConnectionAttempt` JSON object over the `scamalert-driver-events` named pipe and waits for one newline-terminated `DriverDecisionResponse` JSON object.

Pipe name: `scamalert-driver-events`

## Framing

- Encoding is UTF-8 without BOM.
- Each pipe connection carries exactly one request and one response.
- The request is a single JSON object followed by LF.
- The response is a single JSON object followed by LF.
- Readers should also accept CRLF line endings.
- The request must not include a policy field. Policy is owned by the broker.
- Maximum request frame size is 16 KiB before the LF terminator. The broker enforces this while reading the frame and closes the pipe without a response if the limit is exceeded.
- Malformed JSON, invalid UTF-8, unknown enum values, protected-service/destination-port mismatches, blank frames, and partial frames that do not complete before timeout should be treated as protocol failures. The broker should log the failure and close that pipe connection so the next driver event can connect.

## Request

Request JSON:

```json
{"eventId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","occurredAt":"2026-05-06T12:00:00+00:00","sourceIp":"203.0.113.10","destinationPort":3389,"protectedService":"rdp"}
```

Fields:

- `eventId`: unique driver-observed event ID.
- `occurredAt`: UTC timestamp for when the connection attempt was observed.
- `sourceIp`: remote source IP address.
- `destinationPort`: local protected destination port.
- `protectedService`: protected service name such as `rdp`, `ssh`, or `telnet`.

The broker rejects requests where `protectedService` is not one of the known protected services or where `destinationPort` does not map to the same service.

## Response

Response JSON:

```json
{"observedEventId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","decision":"allow","reason":"userSelected"}
```

Fields:

- `observedEventId`: the request `eventId` the broker evaluated.
- `decision`: `allow` or `block`.
- `reason`: broker reason for the decision.

The driver or bridge must verify that `observedEventId` matches the request `eventId`. A mismatch is a protocol failure and must not be applied as the decision for the pending WFP authorization.

## Timeout And Fallback

The broker should bound each connected request with a per-connection timeout and close the pipe on timeout, malformed input, partial input, blank input, or processing failure.

The driver or native user-mode bridge must apply the persisted fail behavior if the broker pipe is unavailable, the protocol fails, or the broker does not return a complete response before the driver timeout. The MVP default fail behavior is allow.

The driver must bound pending WFP authorization time and complete the WFP operation with the final allow/block decision. The driver timeout should be shorter than the maximum authorization time the WFP callout can safely hold.
