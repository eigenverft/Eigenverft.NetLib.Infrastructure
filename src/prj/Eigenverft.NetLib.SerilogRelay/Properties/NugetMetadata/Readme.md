# Eigenverft.NetLib.SerilogRelay

Durable Serilog relay for forwarding application logs over HTTP while keeping a bounded local
persistent spool.

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

Full multi-process sender coordination is still a pre-release work item; see repository
`RELEASE-READINESS.md`.

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

`ApplicationSpool` applies to the shared durable spool:

- `SentEventRetention` is spool-wide;
- `UnsentEventMaxAge` is spool-wide;
- reclamation may remove rows from any process using that spool;
- `MaxPhysicalBytes` is currently a physical ceiling for the whole shared spool.

`Delivery`, `EndpointRetry`, and `EmergencyMemoryBuffer` are runtime settings/state of one
sink/process.

The exact multi-process contract for the shared physical-capacity limit is still being finalized
before release. Do not interpret `MaxPhysicalBytes` as a per-process quota.

## Current reliability behavior

The relay currently provides:

- durable local persistence before normal network delivery;
- stable `EventId` values reused across retries/restarts;
- application-spool-wide sent retention and optional unsent age retention;
- sent-first / oldest-unsent capacity reclamation;
- protection against one individually oversized event evicting existing backlog;
- low-volume delivery after `MaximumBatchWait`;
- immediate startup backlog delivery opportunity;
- process-local exponential endpoint retry with jitter and HTTP `Retry-After`;
- process-local Emergency memory bounds of 16384 events and 64 MiB payload bytes by default;
- a real bounded shutdown deadline;
- idempotent receiver storage by `EventId`.

The implementation targets `net8.0` and `net10.0`.

## Multi-process coordination

Shared-spool senders use atomic short-lived claims/leases so two processes do not intentionally
send the same pending rows at the same time. The internal default lease is 30 seconds.

Current behavior:

- `ProcessId` remains row-origin metadata;
- any process of the same application spool may send old rows from another process;
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

The matched receiver is `Eigenverft.Service.CentralLogging` at:

```text
POST /api/v1/logs
```

Bearer authentication is not implemented yet. Until it is added, keep the receiver inside a
trusted boundary: loopback/private network or behind a trusted reverse proxy/gateway.

`ApplicationId`, `MachineId`, `ProcessId`, and other payload fields are diagnostic/protocol
identity, not authenticated sender identity.

## Current pre-release blockers

See repository `RELEASE-READINESS.md`. The remaining pre-release work is primarily:

- final semantics for shared `ApplicationSpool` settings and the physical spool limit;
- final separate-OS-process smoke validation of the implemented coordination;
- bearer authentication last.

Historical code is background/reference material; current behavior is defined by tests,
`RELIABILITY.md`, and `RELEASE-READINESS.md`.
