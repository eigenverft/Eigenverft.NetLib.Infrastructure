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

## Application spool

Without overrides, the durable spool is application based:

```text
<LocalApplicationData>/Eigenverft/SerilogRelay/<ApplicationId>/SerilogRelay.db
```

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
        SentEventRetention = TimeSpan.FromDays(1),
        UnsentEventMaxAge = null
    },
    Delivery =
    {
        MinimumBatchEvents = 20,
        MaximumBatchEvents = 100,
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
- application-spool-wide sent retention and periodic optional unsent age cleanup;
- sent-first / oldest-eligible-unsent capacity reclamation;
- active-claim protection from unsent age cleanup and capacity reclamation;
- protection against one individually oversized event evicting existing backlog;
- low-volume delivery after `MaximumBatchWait`;
- immediate startup backlog delivery opportunity;
- process-local exponential endpoint retry with jitter and HTTP `Retry-After`;
- process-local Emergency memory bounds of 16384 events and 64 MiB payload bytes by default;
- a real bounded shutdown deadline;
- at-least-once HTTP delivery without imposing receiver-side storage/deduplication semantics.


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

Existing spools are upgraded in place with the claim columns/indexes. Graceful shutdown releases
owned claims immediately; after an ungraceful process exit, expired claims become available to
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
