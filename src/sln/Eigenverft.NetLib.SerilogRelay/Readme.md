# Eigenverft.NetLib.SerilogRelay

Durable Serilog relay for forwarding application logs over HTTP while keeping a bounded local
persistent spool.

For a runnable sender and receiver, see [Run the examples](#run-the-sender-and-receiver-together).

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

For a special proxy, client certificate, or custom authentication, supply a configured
`HttpClient` through `SerilogRelayOptions.HttpClient`. Prefer an application-lifetime client:
bounded logger disposal may return while canceled background cleanup finishes. The sink does not
dispose a supplied client. `Delivery.RequestTimeout` defaults to two
seconds and can be increased below the 30-second claim lease; a shorter timeout on the supplied
client still applies. The built-in clients ignore server cookies, so separate relay sinks cannot
share a cookie container. A supplied client controls its own cookie handling. The sink uses the
same client for spool, RAM, and shutdown delivery.

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

The normalized logical `ApplicationId` accepts up to 256 characters and stays unchanged in
events. IDs longer than a 255-character filesystem component, or reserved Windows device names,
use `_` followed by their SHA-256 hash as the application directory name. Ordinary IDs retain
their existing directory names. An absolute `spoolDirectory` does not depend on the OS user-data
directory; default and relative paths can create a user-data directory that does not yet exist.

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
The resolved version is limited to 256 characters. Longer values fail sink configuration instead
of being truncated, preserving version and commit suffixes.
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
    .WriteTo.SerilogRelay("https://logging.example/api/v1/logs", options)
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

```csharp
var options = new SerilogRelayOptions
{
    ApplicationSpool =
    {
        MaxPhysicalBytes = 64L * 1024L * 1024L,
        SentEventRetention = TimeSpan.FromDays(1),
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
        MaximumBatchWait = TimeSpan.FromSeconds(5)
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

Actively claimed unsent rows are protected from age cleanup and unsent capacity reclamation.
After claim release or lease expiry, they become eligible again. Age cleanup runs periodically
while the sink remains active; the configured age is an eligibility threshold rather than an
exact deletion timestamp.

`Delivery`, `EndpointRetry`, endpoint/bearer configuration, and `EmergencyMemoryBuffer` are
`SentEventRetention` measures age since insertion into the spool (`CreatedAt`), not time since
successful delivery. Acknowledgment does not restart this age. For example, with one-day
retention, a row delivered after two days of backlog is eligible for removal on the next
maintenance pass. Zero deletes acknowledged rows immediately. This option does not expire
unsent rows; `UnsentEventMaxAge` controls their age cleanup. Capacity reclamation may remove
sent rows before the retention age is reached.

runtime settings/state of one sink/process.
`EmergencyMemoryBuffer.MaxBufferedPayloadBytes` counts estimated UTF-8 event-field bytes with
fixed overhead, not exact JSON bytes or total process memory.

## Current reliability behavior

The relay currently provides:

- durable local persistence before normal network delivery;
- stable `EventId` values reused across retries/restarts;
- application-spool-wide sent retention and periodic optional unsent age cleanup;
- sent-first / oldest-eligible-unsent capacity reclamation;
- active-claim protection from unsent age cleanup and capacity reclamation;
- protection against one individually oversized event evicting existing backlog;
- low-volume delivery after `MaximumBatchWait`;
- immediate startup backlog delivery opportunity;
- process-local exponential endpoint retry with jitter and HTTP `Retry-After`;
- process-local Emergency memory bounds of 16384 events and 64 MiB payload bytes by default;
- direct emergency HTTP batches without a minimum count or batch wait, with separate event-count
  and JSON-size targets;
- a real bounded shutdown deadline;
- at-least-once HTTP delivery without imposing receiver-side storage/deduplication semantics.

`Delivery.PollInterval` must be at least one millisecond and no longer than the supported sender
timer delay (about 49.7 days); `EndpointRetry.MaximumDelay` has the same upper bound. A later
HTTP `Retry-After` time remains effective across repeated bounded waits.

During normal operation, the emergency worker retries durable storage for buffered events. If
storage fails, it sends the remaining events in that selected batch directly rather than repeat
the same failing storage operation for every event. Only events still in RAM are sent directly.
It takes currently available RAM events up to
`Delivery.EmergencyMaximumBatchEvents` (256 by default) and
`Delivery.EmergencyTargetBatchPayloadBytes` (4 MiB by default) per HTTP request, then immediately
continues with another bounded batch. There is no minimum count or wait to fill an emergency
batch. An individual event larger than the JSON target is sent alone without truncation.
At shutdown, any already active normal request finishes first. Direct RAM batches run next;
afterward, separate claimed spool batches are sent.
RAM and spool events are not combined in one HTTP batch.


## Multi-process coordination

Shared-spool senders use atomic short-lived claims/leases so two processes do not intentionally
send the same pending rows at the same time. The internal default lease is 30 seconds.
The lease clock starts after acquiring SQLite write access. After preparing the HTTP payload,
the sender verifies ownership of the entire batch and renews its lease. A lost or incomplete
claim is not sent. The request deadline also respects the remaining lease with a one-second margin.

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
- startup generation selection and corruption recovery use separate short-lived cross-process coordination.

Existing spools are upgraded in place with the claim columns/indexes and optional
nullable application-version column. Graceful shutdown releases
owned claims immediately; after an ungraceful process exit, expired claims become available to
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

Any HTTP 2xx received by the current claim owner is treated as successful delivery. Non-2xx or
transport failure keeps the rows unsent and releases the claim before process-local retry
backoff.

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
- a dead-letter queue for non-2xx responses.

Non-2xx deliveries remain unsent for later retry or takeover by another process/version, subject
to configured `UnsentEventMaxAge` and shared-spool capacity reclamation.

## Repository structure

This solution groups the SerilogRelay package and its validation project:

```text
src/sln/Eigenverft.NetLib.SerilogRelay/
  Eigenverft.NetLib.SerilogRelay.slnx
  Readme.md
  RELIABILITY.md

src/prj/Eigenverft.NetLib.SerilogRelay/
  Eigenverft.NetLib.SerilogRelay.csproj
  Properties/version.json
  Properties/NugetMetadata/

src/prj/Eigenverft.NetLib.SerilogRelay.Tests/
  Eigenverft.NetLib.SerilogRelay.Tests.csproj

src/prj/Eigenverft.NetLib.SerilogRelayReceiver.Example/
  Eigenverft.NetLib.SerilogRelayReceiver.Example.csproj

src/prj/Eigenverft.NetLib.SerilogRelaySender.Example/
  Eigenverft.NetLib.SerilogRelaySender.Example.csproj
```

- `Eigenverft.NetLib.SerilogRelay` is the packable product library.
- `Eigenverft.NetLib.SerilogRelay.Tests` contains persistence, delivery, shared-spool, recovery, compatibility, and reliability tests.
- `Eigenverft.NetLib.SerilogRelaySender.Example` sends events through the relay NuGet package.
- `Eigenverft.NetLib.SerilogRelayReceiver.Example` receives events through the receiver NuGet package.
- `RELIABILITY.md` contains the deeper implemented reliability contract and option-scope semantics.

The Solution README intentionally contains the same product description and usage guidance as the NuGet README so the repository can be understood directly without following documentation links.

## Run the sender and receiver together

The sender is a console app using `Eigenverft.NetLib.SerilogRelay` from NuGet. The receiver is an ASP.NET Core app using `Eigenverft.WebLib.SerilogRelayReceiver` from NuGet and storing events in SQLite. Both projects are in this solution and have no product project references.

If you are adding the relay to an application, start with [the sender code](../../prj/Eigenverft.NetLib.SerilogRelaySender.Example/Program.cs). The receiver project supplies a local endpoint for trying it.

Start the receiver from this repository root in the first terminal:

```powershell
dotnet run --project src/prj/Eigenverft.NetLib.SerilogRelayReceiver.Example/Eigenverft.NetLib.SerilogRelayReceiver.Example.csproj
```

Send one message from the same repository root in a second terminal:

```powershell
dotnet run --project src/prj/Eigenverft.NetLib.SerilogRelaySender.Example/Eigenverft.NetLib.SerilogRelaySender.Example.csproj -- "Hello from the example"
```

In the second terminal, read the newest stored message:

```powershell
(Invoke-RestMethod 'http://127.0.0.1:5217/demo/events')[0].renderMessage
```

The result contains `Hello from the example`. The sender writes to a local durable spool and attempts delivery when its logger is disposed. Pending events remain in the spool for a later run if the receiver is unavailable.

Both programs use loopback without authentication for this first example. In an application, configure the receiver's EF Core provider, migrations, endpoint, and authentication for that host; `EnsureCreated` is used here only for the demo. The projects retain Eigenverft metadata and icons but are not packed or published.
