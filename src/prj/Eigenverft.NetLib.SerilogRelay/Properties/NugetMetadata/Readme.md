# Eigenverft.NetLib.SerilogRelay

Durable Serilog relay for forwarding application logs over HTTP while keeping a bounded local
persistent spool.

For the matching ASP.NET Core receiver, use [`Eigenverft.WebLib.SerilogRelayReceiver`](https://www.nuget.org/packages/Eigenverft.WebLib.SerilogRelayReceiver).

## Design intent

The relay bridges periods when an HTTP receiver is unavailable. It stores events durably first, retries delivery in the background, and drains the backlog during normal operation when the receiver becomes available again. The spool is bounded: at capacity, older eligible events make room for newer events.

Shutdown delivery handles the last events of a healthy application run, including batches below the preferred minimum count. It sends immediately and stops when empty or when the configured time budget expires. Outage recovery continues through the durable spool on a later run.

The sender and receiver are deliberately loosely coupled. Any HTTP 2xx is the receiver's acknowledgment that the sender may release the events. The receiver owns its acceptance policy: it may persist, forward, filter, or deliberately discard an event and still acknowledge it. Acknowledgment does not require proof of remote persistence or a particular receiver package. The matching receiver's built-in EF Core handler saves the batch before acknowledgment.

## Supported frameworks

- .NET 8 (`net8.0`)
- .NET 10 (`net10.0`)

## Install

```bash
dotnet add package Eigenverft.NetLib.SerilogRelay
```

## Quick start

```csharp
.WriteTo.SerilogRelay("https://logging.example/api/v1/logs")
```

Normal .NET/platform TLS certificate validation is enabled by default.

## Application spool

Without overrides, the durable spool is application based:

```text
<LocalApplicationData>/Eigenverft/SerilogRelay/<ApplicationId>/SerilogRelay.db
```

The normalized ApplicationId is limited to 256 characters. Longer values fail during configuration rather than being truncated. Without an explicit value, the entry-assembly name is used.

Processes of the same logical application therefore share the same default spool path.

Multiple active sinks can open and persist into that spool. Rows contain `ProcessId`, so their
originating OS process is visible, but rows are not restricted to being sent by their original
process. Another process may drain older backlog from the same application spool.

Shared-spool multi-process claim/lease coordination is implemented.

## Reliability options

```csharp
var options = new SerilogRelayOptions
{
    ApplicationSpool =
    {
        MaxPhysicalBytes = 64L * 1024L * 1024L,
        SentEventRetention = TimeSpan.Zero,
        UnsentEventMaxAge = null
    },
    Delivery =
    {
        MinimumBatchEvents = 20,
        MaximumBatchEvents = 100,
        TargetBatchPayloadBytes = 4 * 1024 * 1024,
        PollInterval = TimeSpan.FromSeconds(5),
        MaximumBatchWait = TimeSpan.FromSeconds(5),
        ShutdownTimeout = TimeSpan.FromSeconds(3),
        ShutdownRequestTimeout = TimeSpan.FromSeconds(1),
        ShutdownRetryInterval = TimeSpan.FromSeconds(1)
    },
    EndpointRetry =
    {
        InitialDelay = TimeSpan.FromSeconds(5),
        Multiplier = 2,
        MaximumDelay = TimeSpan.FromMinutes(5),
        JitterRatio = 0.20,
        RespectRetryAfter = true
    },
    EmergencyMemoryBuffer =
    {
        MaxBufferedEvents = 16_384,
        MaxBufferedPayloadBytes = 64L * 1024L * 1024L
    }
};

.WriteTo.SerilogRelay(
    endpoint: "https://logging.example/api/v1/logs",
    options: options)
```

### Scope of the options

`ApplicationSpool` contains the spool-wide policies this sink/process applies to shared durable storage.

Processes sharing a spool do not negotiate or merge these settings. Each process applies its own
configured values when it performs maintenance or reclamation:

- `SentEventRetention` may remove eligible sent rows created by any process;
- `UnsentEventMaxAge` may remove eligible unsent rows created by any process;
- capacity reclamation may remove eligible rows created by any process;
- `MaxPhysicalBytes` is the physical ceiling applied to the whole shared spool/database, not a
  per-process row quota.

Actively claimed unsent rows are protected from age cleanup and unsent capacity reclamation.
After claim release or lease expiry, they become eligible again. Age cleanup runs periodically
while the sink remains active; the configured age is an eligibility threshold rather than an
exact deletion timestamp.

`Delivery`, `EndpointRetry`, endpoint/bearer configuration, and `EmergencyMemoryBuffer` are
runtime settings/state of one sink/process.

## Current reliability behavior

The relay currently provides:

- durable local persistence before normal network delivery;
- stable `EventId` values reused across retries/restarts;
- immediate deletion after successful acknowledgment by default, optional sent retention, and periodic optional unsent age cleanup;
- sent-first / oldest-eligible-unsent capacity reclamation;
- active-claim protection from unsent age cleanup and capacity reclamation;
- full-schema capacity probing and transactional replacement, preserving backlog when a new event cannot be stored;
- byte-aware batching with a configurable 4 MiB UTF-8 JSON target, including batch metadata and escaping;
- low-volume delivery after `MaximumBatchWait`;
- immediate startup backlog delivery opportunity;
- process-local exponential endpoint retry with jitter and HTTP `Retry-After` on every non-2xx response;
- process-local Emergency memory bounds of 16384 events and 64 MiB payload bytes by default;
- configurable shutdown delivery with a three-second total budget, one-second request cap, and one-second failure retry interval by default;
- at-least-once HTTP delivery without imposing receiver-side storage/deduplication semantics.


## Capacity and emergency behavior

Normal delivery stores events in SQLite first. A successful HTTP 2xx deletes the still-owned claimed rows immediately by default. `SentEventRetention` greater than zero explicitly opts into keeping delivered rows locally.

The insert capacity policy leaves room inside `MaxPhysicalBytes` for delivery claim metadata and index growth. The reserve scales with `MaximumBatchEvents` and is capped at a quarter of the configured page budget for small spools. Both normal inserts and the empty-spool capacity probe apply it.

Already-full spools can be drained without another incoming event: when a claim hits `SQLITE_FULL`, the sender reduces the claim down to one event if necessary. If that still cannot fit, it reclaims sent rows first and then the oldest eligible unsent rows, using the existing bounded capacity policy. Reclamation commits only with a successful claim; failed attempts restore the rows. Reduced claims can be sent below the preferred minimum event count.

When SQLite reaches its configured capacity, the relay first checks whether the new event fits an empty spool with the complete schema, including claim indexes and the metadata reserve. It reclaims sent rows before the oldest eligible unsent rows. Reclamation and replacement commit together; a failed replacement rolls the deletions back.

An event that cannot be stored enters the bounded emergency RAM buffer, whether storage failed with an exception or rejected it because of capacity. The worker retries durable storage and can send directly over HTTP. At either RAM limit, the oldest waiting events are discarded to make room for newer ones, including an old event waiting for another retry. Only a currently executing storage or HTTP attempt retains its reservation. An event too large for the remaining budget after that active reservation is rejected without clearing the queue.

## Batch payload target

`Delivery.TargetBatchPayloadBytes` defaults to 4 MiB. The sender measures the serialized UTF-8 JSON, including escaping, commas, count digits, and batch metadata. It stops filling a batch before another event would exceed the target and releases unused claims before sending. A batch filled by bytes can be sent below `MinimumBatchEvents`; low-volume batches still use `MaximumBatchWait`.

This is a batching target, not an event-size admission limit. An individual event larger than the target is sent alone and without truncation, so the target cannot strand it in the spool. The value belongs to the sender and does not negotiate or impose a receiver body limit. HTTP non-2xx responses retain the events for the existing retry policy.

## Slow responses and HTTP timeouts

Normal delivery currently uses a fixed two-second `HttpClient.Timeout` and a 30-second claim lease. Shutdown adds `Delivery.ShutdownRequestTimeout`, defaulting to one second per request. The request timeout starts with HTTP delivery; the claim lease also covers the preceding local claim/read/payload preparation. Claims are not renewed by receiver activity.

A receiver taking 10, 30, or 60 seconds to finish its response does not extend the client timeout. An HTTP timeout records an endpoint failure, leaves durable events unacknowledged, and releases the still-owned claim for retry. If claim release fails or the process exits first, the lease provides the fallback. Only one client HTTP attempt per sink is active at a time; a timed-out server operation may still overlap later retries or attempts from other processes.

`PostAsync` waits for the entire response, including its body. Even received 2xx headers cannot acknowledge a response whose body stalls past the timeout. A completed response on a connection kept open for HTTP keep-alive is already complete and can be acknowledged normally. During shutdown, the remaining shutdown budget may cancel a request earlier.

A server can commit successfully after the client has timed out. Retries preserve `EventId` but create a new `BatchId`; the receiver owns handling repeat delivery, including any deduplication it requires. The relay cannot infer the outcome of a request without a completed 2xx response.

## Shutdown

`Delivery.ShutdownTimeout` is an upper limit, not a fixed wait. Shutdown finishes as soon as the emergency buffer and durable spool have been handled. Set it to `TimeSpan.Zero` to skip shutdown delivery. The default is three seconds. `Delivery.ShutdownRequestTimeout` adds a one-second request cap by default, also applied from shutdown start to a request already in progress. The normal two-second HTTP timeout and remaining total budget still apply; whichever expires first ends the request.

`Delivery.ShutdownRetryInterval` defaults to one second and sets the minimum retry interval after failure. Time already spent in the last failed HTTP attempt counts toward this interval; only the remainder is waited. A one-second timeout therefore does not incur another full second of retry delay. With the defaults, failed attempts can start approximately at zero, one, and two seconds within the three-second budget, subject to local work and scheduling. Faster successful requests continue immediately.

If a normal sender batch is already in progress, shutdown lets the current attempt finish within the applicable limits and acknowledges it on success, then stops the normal round before another batch. Retry/error waits yield to shutdown as well. Emergency delivery waits for this handover before acquiring the HTTP gate.

Shutdown then sends volatile emergency events first and flushes the spool even below `MinimumBatchEvents`. Successful batches have no normal inter-batch pause. The final delivery attempts bypass the normal endpoint backoff, while only one HTTP attempt remains active at a time.

When the budget expires, `Dispose` returns and pending durable rows remain available for a later run. SQLite calls already executing may finish afterward; their resources are released when background cleanup completes. Claims that cannot be released become available after their 30-second lease expires. Remaining RAM events are best-effort delivery only.

## Multi-process coordination

Shared-spool senders use atomic short-lived claims/leases so two processes do not intentionally
send the same pending rows at the same time. The internal default lease is 30 seconds.

Current behavior:

- `ProcessId` remains row-origin metadata;
- any process of the same application spool may send old rows from another process;
- pending rows are not bound to the endpoint or bearer token of the process that created them;
- the process owning the current claim sends with its own configured endpoint and bearer token;
- non-2xx releases the claim before that process enters retry backoff, allowing another version
  to take over; a 2xx received by the current claim owner marks the claimed rows delivered;
- only one sender owns a row's active claim at a time;
- expired claims become available after process death;
- the sending process uses its own `Delivery` and `EndpointRetry` settings;
- the spool is periodically checked for work created by other processes;
- physical corruption recovery receives separate short-lived cross-process coordination.

Existing spools are upgraded in place with the claim columns/indexes. Graceful shutdown attempts to release
owned claims within its time budget; after an ungraceful process exit, expired claims become available to
another sender. A stale sender cannot mark a row sent after another sender has taken over its
expired claim.

## Receiver/security scope

SerilogRelay targets a generic HTTP receiver. The endpoint is supplied by the application and is
not persisted with individual spool rows.

Any HTTP 2xx received by the current claim owner is treated as successful delivery. Non-2xx or
transport failure keeps the rows unsent and releases the claim before process-local retry
backoff.

Bearer authentication is supported with one optional opaque token:

```csharp
.WriteTo.SerilogRelay(
    endpoint: "https://logging.example/api/v1/logs",
    bearerToken: "replace-with-secret")
```

Pass only the token value, not the `Bearer ` scheme prefix. Null, empty, or whitespace means no
Authorization header is sent. The token belongs to the sending sink/process and is not persisted
with spool rows, so an updated process draining old backlog uses its own current token.

SerilogRelay does not parse JWT claims or perform token refresh. Receiver persistence,
duplicate-handling, and server-side storage policies are receiver concerns.

`ApplicationId`, `MachineId`, `ProcessId`, and other payload fields remain
diagnostic/protocol identity, not authenticated sender identity.

## Scope boundaries

SerilogRelay intentionally does not define receiver-side persistence, duplicate presentation, or
server-side storage policy.

The current sink design also does not introduce:

- alternate spool storage backends or an ORM/provider abstraction;
- a server-driven configuration/handshake protocol;
- an application-wide retry gate or shared `Retry-After` cooldown across processes;
- a dead-letter queue for non-2xx responses.

Non-2xx deliveries remain unsent for later retry or takeover by another process/version, subject
to configured `UnsentEventMaxAge` and shared-spool capacity reclamation.
