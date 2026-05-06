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
- Recommended maximum frame size is 16 KiB. A reader that receives a frame larger than its limit should treat it as malformed, close the pipe, and apply its fallback behavior.
- Malformed JSON, unknown enum values, blank frames, and partial frames that do not complete before timeout should be treated as protocol failures. The broker should log the failure and close that pipe connection so the next driver event can connect.

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

## Response

Response JSON:

```json
{"observedEventId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","decision":"allow","reason":"userSelected"}
```

Fields:

- `observedEventId`: the request `eventId` the broker evaluated.
- `decision`: `allow` or `block`.
- `reason`: broker reason for the decision.

## Timeout And Fallback

The broker should bound each connected request with a per-connection timeout and close the pipe on timeout, malformed input, partial input, blank input, or processing failure.

The driver or native user-mode bridge must apply the persisted fail behavior if the broker pipe is unavailable, the protocol fails, or the broker does not return a complete response before the driver timeout. The MVP default fail behavior is allow.

The driver must bound pending WFP authorization time and complete the WFP operation with the final allow/block decision. The driver timeout should be shorter than the maximum authorization time the WFP callout can safely hold.
