# Eigenverft.NetLib.SerilogRelay

Durable Serilog relay for forwarding application logs over HTTP while keeping a bounded local
persistent spool.

For the matching ASP.NET Core receiver, use [`Eigenverft.WebLib.SerilogRelayReceiver`](https://www.nuget.org/packages/Eigenverft.WebLib.SerilogRelayReceiver).

## Design intent

The relay bridges periods when an HTTP receiver is unavailable. It stores events durably first, retries delivery in the background, and drains the backlog during normal operation when the receiver becomes available again. The spool is bounded: at capacity, older eligible events make room for newer events.

Shutdown delivery handles the last events of a healthy application run, including batches below the preferred minimum count. It sends immediately and stops when empty or when the configured time budget expires. Outage recovery continues through the durable spool on a later run.

The sender and receiver are deliberately loosely coupled. Only a complete HTTP **204 No Content** is the receiver's acknowledgment that the sender may release the events. The receiver owns its acceptance policy: it may persist, forward, filter, or deliberately discard an event and still acknowledge it after processing the entire batch. No response body or extra acknowledgment header is required. Acknowledgment does not require proof of remote persistence or a particular receiver package. The matching receiver's built-in EF Core handler saves the batch before acknowledgment.

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

`ApplicationId` is normalized before it is recorded on events or used by the default spool path.
After trimming surrounding whitespace, each run of characters outside `A-Z`, `a-z`, `0-9`, `.`,
`_`, and `-` becomes one `_`; leading and trailing `.` and `_` are removed. An empty result
becomes `Application`. Without an explicit value, the entry-assembly name is used.
For example, `Payroll/Worker` and `Payroll:Worker` both become `Payroll_Worker`: they identify
the same logical application and share the default spool. Choose distinct normalized IDs for
separate applications, and ensure their resolved spool paths are distinct.

Processes of the same logical application therefore share the same default spool path.

The effective `ApplicationId` never exceeds 255 ASCII characters. Normalized IDs of at
most 255 characters stay unchanged. Longer IDs become `_` + their first 189 normalized
characters + `_` + the lowercase SHA-256 hash of the complete normalized ID (64 hexadecimal
characters). The result is exactly 255 ASCII characters. The leading `_` reserves this
representation because ordinary normalized IDs cannot start with it. Long IDs with the
same readable prefix therefore retain distinct hash suffixes.

The effective ID is recorded on each new event and used as the default application directory
name. Reserved Windows device names still use `_` followed by their SHA-256 hash as the
directory name while retaining their event identity. Ordinary IDs keep their existing paths.
An absolute `spoolDirectory` does not depend on the OS user-data directory; default and
relative paths can create a user-data directory that does not yet exist.

Multiple active sinks can open and persist into that spool. Rows contain `ProcessId`, so their
originating OS process is visible, but rows are not restricted to being sent by their original
process. Another process may drain older backlog from the same application spool.

Shared-spool multi-process claim/lease coordination is implemented.

## Application version on events

New events carry `ApplicationVersion` from the consuming application's entry assembly. The sink
resolves its informational version once at startup, falling back to the assembly version when
needed. A generic host can override it with `SerilogRelayOptions.ApplicationVersion`.
The value is stored on each event, including emergency-memory events, so a newer process that
sends older spool rows does not replace their origin version. Rows created before this field was
added have no version, and their HTTP payload omits `applicationVersion`.
Surrounding whitespace is removed. Versions longer than 255 UTF-16 code units retain their
prefix without splitting a Unicode surrogate pair, for both assembly metadata and explicit
overrides. An explicit blank override remains invalid. Shortening can remove a trailing
commit suffix; configure a shorter version if that suffix must be retained.

This 255-character contract applies to new events. Existing spool rows keep their stored
identity and version. Older 256-character IDs now resolve to a different effective identity
and default spool directory. A receiver enforcing the new limit rejects historical
256-character values; coordinate package upgrades and drain incompatible backlog beforehand.
To retain this metadata with the matching receiver, use its updated contract. An older receiver
can acknowledge a batch while ignoring the new field.

## Relay status events

Relay operational events are off by default. To see important relay problems and their recovery
at the receiver, configure:

```csharp
var options = new SerilogRelayOptions();
options.StatusEvents.Mode = SerilogRelayStatusEventMode.RelayOnly;
options.StatusEvents.MinimumLevel = Serilog.Events.LogEventLevel.Warning;
```

`RelayOnly` sends status events through this relay without writing them to the application's
other Serilog sinks. It uses `StatusEvents.MinimumLevel` independently of the application's
logger and the Relay sink's `restrictedToMinimumLevel`. `AllSinks` writes through the application
logger, so its level filters and the Relay sink's own minimum level must allow each status event
that should reach the receiver. `AllSinks` requires `StatusEvents.LoggerProvider`; the delegate
must return null until `CreateLogger()` has completed, whether the application uses a local or
global logger. This keeps startup status transitions pending for a later attempt instead of
writing them to an earlier, possibly silent logger. For example:

```csharp
Serilog.ILogger? applicationLogger = null;
options.StatusEvents.Mode = SerilogRelayStatusEventMode.AllSinks;
options.StatusEvents.LoggerProvider = () => applicationLogger;
applicationLogger = new LoggerConfiguration()
    .WriteTo.SerilogRelay("https://logging.example/api/v1/logs", options: options)
    .CreateLogger();
```

`Off` keeps Serilog's separate `SelfLog` diagnostics available.

Warnings describe spool failures, HTTP delivery failures, recovery, and buffer or spool-budget
pressure; errors summarize dropped events. Startup is Information. An optional Debug summary is
enabled by setting both `MinimumLevel = LogEventLevel.Debug` and `SummaryInterval` to at least
one minute. No event is emitted for every request or batch. Status events carry structured
backlog, emergency-buffer, drop-count, and last-success fields. Spool-budget percentage refers
to SQLite's configured page budget, not free space on the filesystem.

Spool, HTTP, and emergency-buffer changes wake the status worker when they occur; it does not
poll them every second. Up to 64 pending transitions retain their individual timestamps and
order. Further transitions are summarized with counts and their first and last times, while
cumulative transition counters remain on status events. The shared spool's SQLite budget is
sampled every 30 seconds; the optional Debug summary uses its configured interval. Pending
transitions remain volatile until their status events reach the spool or application logger;
process shutdown can lose transitions that have not yet been published.
In `RelayOnly`, a transition remains pending for retry when neither storage path accepts it.

Status events use the same spool, emergency RAM buffer, batching, and shutdown delivery as
application events. On admission they can use free capacity and reclaim sent spool rows, but do
not evict waiting spool or RAM events. They still occupy capacity; later normal reclamation can
evict eligible unsent rows and report that loss. When neither buffer has room, a `RelayOnly`
transition remains pending for retry.
In `AllSinks` mode, Console or other sinks may receive a status event even when Relay cannot
retain its copy. Dropped status events in RAM remain in cumulative counters without generating
another loss report.

Emergency pressure thresholds count application events only, so status events cannot generate
their own repeating pressure warnings. All events still count toward the actual RAM capacity.
Storage failures observed by delivery, maintenance, and diagnostic queries use the same spool
state transitions as Emit. Successful writes report recovery; a successful read alone does not
claim that durable persistence has resumed.

## Reliability options

`SerilogRelay` has one configuration method. Omit `options` for defaults or pass
`options: relayOptions` for grouped settings. Batch sizes, intervals, and retention are configured
through `Delivery` and `ApplicationSpool`; they are not separate sink-method parameters.
The existing positional `endpoint` and spool-path parameters remain available, including
`.SerilogRelay(endpoint, null)` for the default spool directory.

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
        RequestTimeout = TimeSpan.FromSeconds(2),
        MinimumBatchEvents = 20,
        MaximumBatchEvents = 100,
        TargetBatchPayloadBytes = 4 * 1024 * 1024,
        EmergencyMaximumBatchEvents = 256,
        EmergencyTargetBatchPayloadBytes = 4 * 1024 * 1024,
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
- `MaxPhysicalBytes` sets the SQLite page budget for the shared database, not a per-process row
  quota or a hard limit on all files. An existing larger database is not shrunk.

`SentEventRetention` measures age since insertion into the spool (`CreatedAt`), not time since
successful delivery. Acknowledgment does not restart this age. For example, with one-day
retention, a row delivered after two days of backlog is eligible for removal on the next
maintenance pass. Zero deletes acknowledged rows immediately. This option does not expire
unsent rows; `UnsentEventMaxAge` controls their age cleanup. Capacity reclamation may remove
sent rows before the retention age is reached.

Actively claimed unsent rows are protected from age cleanup and unsent capacity reclamation.
After claim release or lease expiry, they become eligible again. Age cleanup runs periodically
while the sink remains active; the configured age is an eligibility threshold rather than an
exact deletion timestamp.

`Delivery`, `EndpointRetry`, endpoint/bearer configuration, and `EmergencyMemoryBuffer` are
runtime settings/state of one sink/process.
`EmergencyMemoryBuffer.MaxBufferedPayloadBytes` counts estimated UTF-8 event-field bytes with
fixed overhead, not exact JSON bytes or total process memory.

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
- process-local exponential endpoint retry with jitter and HTTP `Retry-After` on every non-204 response;
- process-local Emergency memory bounds of 16384 events and 64 MiB payload bytes by default;
- direct emergency HTTP batches without a minimum count or batch wait, with separate event-count
  and JSON-size targets;
- configurable shutdown delivery with a three-second total budget, one-second request cap, and one-second failure retry interval by default;
- at-least-once HTTP delivery without imposing receiver-side storage/deduplication semantics.

`Delivery.PollInterval` must be at least one millisecond and no longer than the supported sender
timer delay (about 49.7 days); `EndpointRetry.MaximumDelay` has the same upper bound.
`MaximumDelay` caps both local backoff and HTTP `Retry-After`, with a default of five minutes.
A server hint can extend local backoff up to this cap, but cannot shorten it. The cap applies
to both delta-seconds and absolute dates, including a hint a year in the future.


## Capacity and emergency behavior

Normal delivery stores events in SQLite first. A successful HTTP 204 deletes the still-owned claimed rows immediately by default. `SentEventRetention` greater than zero explicitly opts into keeping delivered rows locally.

The insert capacity policy leaves room inside `MaxPhysicalBytes` for delivery claim metadata and index growth. The reserve scales with `MaximumBatchEvents` and is capped at a quarter of the configured page budget for small spools. Both normal inserts and the empty-spool capacity probe apply it.

Already-full spools can be drained without another incoming event: when a claim hits `SQLITE_FULL`, the sender reduces the claim down to one event if necessary. If that still cannot fit, it reclaims sent rows first and then the oldest eligible unsent rows, using the existing bounded capacity policy. Reclamation commits only with a successful claim; failed attempts restore the rows. Reduced claims can be sent below the preferred minimum event count.

When SQLite reaches its configured capacity, the relay first checks whether the new event fits an empty spool with the complete schema, including claim indexes and the metadata reserve. It reclaims sent rows before the oldest eligible unsent rows. Reclamation and replacement commit together; a failed replacement rolls the deletions back.

An event that cannot be stored enters the bounded emergency RAM buffer, whether storage failed with an exception or rejected it because of capacity. The worker retries durable storage and can send directly over HTTP. At either RAM limit, the oldest waiting events are discarded to make room for newer ones, including an old event waiting for another retry. Events selected into an active batch retain their reservation until the attempt ends; a failed batch becomes eligible for oldest-waiting eviction again. An event too large for the remaining budget after that active reservation is rejected without clearing the queue.

The emergency worker retries durable storage during normal operation. If storage fails for an event, it sends the remaining events in that selected batch directly rather than repeat the same failing storage operation for every event. It sends only events still in RAM directly, taking currently available entries up to `Delivery.EmergencyMaximumBatchEvents` (256 by default) and `Delivery.EmergencyTargetBatchPayloadBytes` (4 MiB by default) per request. There is no minimum count or wait to fill a direct emergency batch. Further RAM batches follow immediately after success. A failed batch remains in RAM for retry under the existing buffer bounds and endpoint backoff.

## Batch payload target

`Delivery.TargetBatchPayloadBytes` defaults to 4 MiB. The sender measures the serialized UTF-8 JSON, including escaping, commas, count digits, and batch metadata. It stops filling a batch before another event would exceed the target and releases unused claims before sending. A batch filled by bytes can be sent below `MinimumBatchEvents`; low-volume batches still use `MaximumBatchWait`.

This is a batching target, not an event-size admission limit. An individual event larger than the target is sent alone and without truncation, so the target cannot strand it in the spool. The value belongs to the sender and does not negotiate or impose a receiver body limit. HTTP non-204 responses retain the events for the existing retry policy.

The direct emergency HTTP path has independent count and JSON-size targets. Its JSON target likewise allows one individually oversized event to be attempted alone. RAM and spool events are never combined in one HTTP request.

The matching receiver defaults to accepting 256 events per request. It therefore accepts
both sender count defaults (100 spool / 256 emergency), including during shutdown, without
an options override. If either sender count maximum changes, keep the receiver maximum at
least as large as both. Host/proxy byte limits and request timeouts remain independent.

## HTTP transport

By default, the sink owns its HTTP client and uses normal .NET certificate validation and the
platform's default proxy settings. Built-in clients ignore server cookies so separate relay
sinks cannot share a cookie container. A supplied client controls its own cookie handling.
For a special proxy, proxy credentials, client certificate, or custom authentication handler,
pass a configured client through `SerilogRelayOptions.HttpClient`:

```csharp
var relayClient = new HttpClient(new SocketsHttpHandler
{
    Proxy = applicationProxy,
    PooledConnectionLifetime = TimeSpan.FromMinutes(5)
});

var options = new SerilogRelayOptions { HttpClient = relayClient };
options.Delivery.RequestTimeout = TimeSpan.FromSeconds(10);

using var logger = new LoggerConfiguration()
    .WriteTo.SerilogRelay("https://logging.example/api/v1/logs", options: options)
    .CreateLogger();
```

Here `applicationProxy` is the application's configured `IWebProxy`, which can carry proxy
credentials. Keep the client for the application's lifetime; the sink does not change or dispose
it. Bounded logger disposal can return while canceled background cleanup finishes. A
factory-created client captured for the sink's lifetime needs an appropriate handler connection
lifetime for DNS changes. Custom handlers can provide changing credentials; avoid unbounded extra
HTTP retries because the relay already retries failed deliveries.

The existing `bearerToken` parameter still applies with a supplied client. When present, it sets
`Authorization: Bearer` on each relay request and takes precedence over the client's default
Authorization header. Without it, the client's own authentication applies. Configure certificate
validation on the supplied client's handler; combining it with
`dangerousAcceptAnyServerCertificate: true` is rejected.

The same client sends normal spool batches, direct emergency batches, and shutdown batches.

## Slow responses and HTTP timeouts

`Delivery.RequestTimeout` defaults to two seconds and must be positive and shorter than the
30-second claim lease. It limits each HTTP request without changing a supplied client. That
client's own `HttpClient.Timeout` can end a request earlier. Shutdown adds
`Delivery.ShutdownRequestTimeout`, defaulting to one second per request. The request timeout
starts with HTTP delivery. The initial claim clock starts after acquiring SQLite write access.
After preparing the payload, the sender verifies ownership of the entire batch and renews its
lease immediately before HTTP delivery. A lost or incomplete claim is not sent. The request is
also limited to the remaining renewed lease minus a one-second margin; if no usable time remains,
the attempt is skipped. Receiver activity does not renew claims.

A receiver taking 10, 30, or 60 seconds to finish its response does not extend the client timeout. An HTTP timeout records an endpoint failure, leaves durable events unacknowledged, and releases the still-owned claim for retry. If claim release fails or the process exits first, the lease provides the fallback. Only one client HTTP attempt per sink is active at a time; a timed-out server operation may still overlap later retries or attempts from other processes.

Relay reads and discards the entire response body as it arrives, without buffering it as a whole.
The built-in client limits complete response bodies to 4 KiB. A supplied client's
`MaxResponseContentBufferSize` remains its response-size limit, including when the body length
is unknown; the sink does not change it. Request, client, and shutdown timeouts cover both headers and body.
Only a complete HTTP 204 response within these limits confirms delivery. HTTP 204 carries no
response body, and a connection kept open for HTTP keep-alive does not delay acknowledgment.
Any response-body failure leaves the batch unacknowledged. During shutdown, the remaining
shutdown budget may cancel a request earlier.

A valid `Retry-After` received with non-204 headers still controls failure pacing if reading the
body fails, including on a size-limit rejection or request timeout. The hint is capped by
`EndpointRetry.MaximumDelay`. The batch remains unacknowledged; using the retry hint does not
accept the failed response as a successful delivery.

A server can commit successfully after the client has timed out. Retries preserve `EventId` but create a new `BatchId`; the receiver owns handling repeat delivery, including any deduplication it requires. The relay cannot infer the outcome of a request without a completed HTTP 204 response.

## Shutdown

`Delivery.ShutdownTimeout` is an upper limit, not a fixed wait. Shutdown finishes as soon as the emergency buffer and durable spool have been handled. Set it to `TimeSpan.Zero` to skip shutdown delivery. The default is three seconds. `Delivery.ShutdownRequestTimeout` adds a one-second request cap by default, also applied from shutdown start to a request already in progress. The configured `Delivery.RequestTimeout`, any shorter timeout on a supplied client, and the remaining total budget still apply; whichever expires first ends the request.

`Delivery.ShutdownRetryInterval` defaults to one second and sets the minimum retry interval after failure. Time already spent in the last failed HTTP attempt counts toward this interval; only the remainder is waited. A one-second timeout therefore does not incur another full second of retry delay. With the defaults, failed attempts can start approximately at zero, one, and two seconds within the three-second budget, subject to local work and scheduling. Faster successful requests continue immediately.

If a normal sender batch is already in progress, shutdown lets the current attempt finish within the applicable limits and acknowledges it on success, then stops the normal round before another batch. Retry/error waits yield to shutdown as well. Emergency delivery waits for this handover before acquiring the HTTP gate.

Shutdown then sends volatile emergency events in bounded RAM batches first and flushes the spool in separate claimed batches even below `MinimumBatchEvents`. The RAM batches use the emergency count and JSON targets; the spool batches use the normal count and JSON targets. Successful batches have no normal inter-batch pause. The final delivery attempts bypass the normal endpoint backoff, while only one HTTP attempt remains active at a time.

When the budget expires, `Dispose` returns and pending durable rows remain available for a later run. SQLite calls already executing may finish afterward; their resources are released when background cleanup completes. Claims that cannot be released become available after their 30-second lease expires. Remaining RAM events are best-effort delivery only.

## Multi-process coordination

Shared-spool senders use atomic short-lived claims/leases so two processes do not intentionally
send the same pending rows at the same time. The internal default lease is 30 seconds.

Current behavior:

- `ProcessId` remains row-origin metadata;
- any process of the same application spool may send old rows from another process;
- pending rows are not bound to the endpoint or bearer token of the process that created them;
- the process owning the current claim sends with its own configured endpoint and bearer token;
- non-204 releases the claim before that process enters retry backoff, allowing another version
  to take over; a complete HTTP 204 received by the current claim owner marks the claimed rows delivered;
- only one sender owns a row's active claim at a time;
- expired claims become available after process death;
- the sending process uses its own `Delivery` and `EndpointRetry` settings;
- the spool is periodically checked for work created by other processes;
- startup generation selection and corruption recovery use separate short-lived cross-process coordination.

Existing spools are upgraded in place with the claim columns/indexes and optional
nullable application-version column. Graceful shutdown attempts to release
owned claims within its time budget; after an ungraceful process exit, expired claims become available to
another sender. A stale sender cannot mark a row sent after another sender has taken over its
expired claim.

## Spool generations after corruption

Each sink instance selects the highest numeric spool generation once at startup and keeps it
for its lifetime: `SerilogRelay.db`, `SerilogRelay.g0001.db`, `SerilogRelay.g0002.db`, and so on.
Custom spool filenames use the same `.gNNNN` suffix before their extension.

On `SQLITE_CORRUPT` or `SQLITE_NOTADB`, the affected instance permanently stops all SQLite
access, including claims, diagnostics, maintenance, and shutdown spool delivery. It continues
with the configured RAM buffer and direct HTTP batches. Under the shared recovery lock it
reserves the next generation, unless another process has already done so. The successor starts
empty and its schema is initialized by a new sink instance. Existing healthy instances can
continue using their older generation; no open database is renamed or replaced.

At startup, older generations and their WAL/SHM files can be deleted after a proven change of
OS boot session. An internal Windows/Linux/macOS helper reads the platform boot identifier.
The existing `<base-spool-filename>.recovery.lock` records the latest generation number and
its boot identifier; no additional metadata file or SQL table is created. Missing, incomplete,
or mismatched records defer cleanup until a later boot. Wall-clock changes and file timestamps
are not used as reboot evidence. Unsupported platforms can still spool and recover into new
generations, but skip automatic deletion.

This protocol requires participating sink versions to select generations at startup. Corrupt
older data is not salvaged; recovery preserves the next generation's operation. The configured
spool budget applies to each active database, not retained obsolete generations. If generation
selection cannot be coordinated at startup, that instance stays in RAM mode until replaced.

## Receiver/security scope

SerilogRelay targets a generic HTTP receiver. The endpoint is supplied by the application and is
not persisted with individual spool rows.

Only a completed **204 No Content** confirms delivery of the entire batch, for spool, emergency
RAM, and shutdown delivery alike. HTTP 200, 201, 202, and every other status keep events
unacknowledged. Durable rows remain unsent and their claims are released before process-local
retry backoff; RAM events remain in the emergency retry path.

Redirects follow the HTTP handler's policy; the final response must still be 204. A login page
returning HTTP 200 therefore cannot acknowledge delivery. Receivers that previously returned
another successful status must change their acknowledgment to 204.

Bearer authentication is supported with one optional opaque token:

```csharp
.WriteTo.SerilogRelay(
    endpoint: "https://logging.example/api/v1/logs",
    bearerToken: "replace-with-secret")
```

Pass only the token value, not the `Bearer ` scheme prefix. Null, empty, or whitespace means the
sink does not add an Authorization header; a supplied client may still provide one. The token
belongs to the sending sink/process and is not persisted with spool rows, so an updated process
draining old backlog uses its own current token.

SerilogRelay does not parse JWT claims or refresh its `bearerToken` parameter. A supplied client
can implement its own authentication handler. Receiver persistence,
duplicate-handling, and server-side storage policies are receiver concerns.

`ApplicationId`, `ApplicationVersion`, `MachineId`, `ProcessId`, and other payload fields remain
diagnostic/protocol identity, not authenticated sender identity.

## Scope boundaries

SerilogRelay intentionally does not define receiver-side persistence, duplicate presentation, or
server-side storage policy.

The current sink design also does not introduce:

- alternate spool storage backends or an ORM/provider abstraction;
- a server-driven configuration/handshake protocol;
- an application-wide retry gate or shared `Retry-After` cooldown across processes;
- a dead-letter queue for non-204 responses.

Non-204 deliveries remain unsent for later retry or takeover by another process/version, subject
to configured `UnsentEventMaxAge` and shared-spool capacity reclamation.
