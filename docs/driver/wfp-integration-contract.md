# WFP Driver Integration Contract

The broker owns policy. The native WFP driver owns kernel observation and WFP completion. `ScamAlert.DriverBridge` is the user-mode adapter between those two boundaries:

- DriverBridge talks to the native driver through binary IOCTLs on `\\.\ScamAlertWfp`.
- DriverBridge talks to `ScamAlert.Broker` by sending one newline-terminated `ProtectedConnectionAttempt` JSON object over the `scamalert-driver-events` named pipe and waiting for one newline-terminated `DriverDecisionResponse` JSON object.

Pipe name: `scamalert-driver-events`

Device name: `\\.\ScamAlertWfp`

## Named-Pipe Framing

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

## Binary IOCTL Boundary

The native contract is defined in `native/ScamAlert.Driver.Shared/ScamAlertDriverIoctl.h` and mirrored by `src/ScamAlert.DriverBridge/Driver/NativeDriverContracts.cs`.

Supported IOCTLs:

- `IOCTL_SCAMALERT_GET_EVENT`: DriverBridge requests the next queued driver event. The call is currently non-blocking: it returns an event immediately when one is available, or `STATUS_NO_MORE_ENTRIES` when the queue is empty. The bridge handles an empty queue by polling again after a short delay.
- `IOCTL_SCAMALERT_COMPLETE_EVENT`: DriverBridge returns the allow/block decision for a pending event.
- `IOCTL_SCAMALERT_GET_STATS`: diagnostics-only counter snapshot.

Current native structure sizes:

- `SCAMALERT_CONNECTION_EVENT`: 122 bytes
- `SCAMALERT_CONNECTION_DECISION`: 20 bytes
- `SCAMALERT_DRIVER_STATS`: 72 bytes

The driver device is restricted to LocalSystem and administrators. The bridge should run elevated or as a service account that can open the device.

Current stats counters:

- `ClassifyEntered`
- `SelfInjectedSkipped`
- `EventsQueued`
- `PendOk`
- `AllowInjected`
- `BlockReleased`
- `TimedOutFailBlock`
- `EventsDropped`
- `PendingRejected`

## Timeout And Fallback

The broker should bound each connected request with a per-connection timeout and close the pipe on timeout, malformed input, partial input, blank input, or processing failure.

DriverBridge currently applies the MVP fail behavior if the broker pipe is unavailable, the protocol fails, or the broker does not return a complete response before the bridge timeout. The MVP default fail behavior is allow, matching the simulator-era local flow.

The driver also bounds pending WFP authorization time. If no completion reaches the kernel before the pending-operation timeout, the driver fails the stale operation closed and increments `TimedOutFailBlock`.

The event queue and pending-operation table are bounded. Under pressure, the driver increments `EventsDropped` or `PendingRejected` instead of allowing unbounded nonpaged-pool growth.
