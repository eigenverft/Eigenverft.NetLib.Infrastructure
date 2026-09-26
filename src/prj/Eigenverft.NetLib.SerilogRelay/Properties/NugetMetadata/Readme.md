# Eigenverft.NetLib.SerilogRelay

Durable Serilog relay for forwarding application logs over HTTP while keeping a bounded local
persistent spool.

## Quick start

The normal configuration remains intentionally small:

```csharp
.WriteTo.SerilogRelay("https://logging.example/api/v1/logs")
```

The relay uses normal .NET/platform TLS certificate validation by default.

For deliberately untrusted/self-signed development infrastructure only:

```csharp
.WriteTo.SerilogRelay(
    endpoint: "https://logging.example/api/v1/logs",
    dangerousAcceptAnyServerCertificate: true)
```

`dangerousAcceptAnyServerCertificate: true` disables server-certificate validation. It does
not authenticate the receiver and should not be used as a production trust mechanism.

## Supported v1 deployment scope

Version 1 supports exactly **one active SerilogRelay sink per file-backed spool path**.

The sink holds an exclusive lease for the lifetime of the spool. A second active sink/process
using the same path is rejected immediately. Multiple processes are supported when they use
distinct spool paths; shared-spool multi-process sender coordination is not a v1 feature.

Bearer authentication is not implemented yet. Until it is added, use the receiver only inside
a trusted boundary:

- loopback/local-machine;
- a private/trusted network;
- or behind a trusted reverse proxy/gateway that controls external access.

Do not expose an unauthenticated CentralLogging ingestion endpoint directly to an untrusted or
public network.

`ApplicationId`, `MachineId`, `ProcessId`, and other payload identity fields are
diagnostic/protocol metadata, not authenticated sender identity.

See repository-level `RELEASE-READINESS.md` for the current release gates and operating
contract.

## Local storage

Without spool overrides, the relay resolves its persistent local spool below
`Environment.SpecialFolder.LocalApplicationData`:

```text
<Eigenverft local application data>/Eigenverft/SerilogRelay/<ApplicationId>/SerilogRelay.db
```

`ApplicationId` defaults to the entry-assembly name, falling back to the current AppDomain
friendly name, and is normalized for safe directory use.

Storage location and application identity can be overridden:

```csharp
.WriteTo.SerilogRelay(
    endpoint: "https://logging.example/api/v1/logs",
    spoolDirectory: @"D:\AppData\Logging",
    spoolFileName: "MyWorker.db",
    applicationId: "MyWorker")
```

A relative `spoolDirectory` is resolved below the application-specific default relay
directory; an absolute directory is used as supplied. `spoolFileName` is filename-only.

Calling `.WriteTo.SerilogRelay()` with no endpoint keeps durable local storage enabled without
starting the HTTP sender.

## Reliability options

Most applications should use defaults. Advanced callers can use `SerilogRelayOptions`:

```csharp
var options = new SerilogRelayOptions
{
    LocalStorage =
    {
        MaxBytes = 64L * 1024L * 1024L,
        SentRetention = TimeSpan.FromDays(1),
        UnsentMaxAge = null
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

## Current behavior

The relay currently provides:

- durable local persistence before normal network delivery;
- a 64 MiB default local-storage budget;
- sent-row retention of one day by default;
- no default age expiry for unsent backlog;
- low-volume delivery after `MaximumBatchWait`, even below the preferred minimum batch size;
- immediate startup delivery opportunity for existing backlog;
- stable per-event `EventId` values reused across retries/restarts;
- protocol version `1` batches for `Eigenverft.Service.CentralLogging`;
- producer metadata `ApplicationId`, pseudonymous `MachineId`, and `ProcessId`;
- one shared exponential endpoint `RetryGate` with jitter and HTTP `Retry-After`;
- immediate retry-state reset after successful delivery;
- a bounded Emergency memory fallback for local-persistence failure only;
- Emergency bounds of 16384 events and 64 MiB serialized payload bytes by default;
- direct Emergency HTTP rescue using the same endpoint RetryGate;
- explicit local-spool capacity loss diagnostics rather than spilling capacity rejection into
  Emergency RAM;
- protection against one individually oversized event evicting existing unsent backlog;
- restart-safe durable backlog;
- contained corruption recovery for the current SQLite storage implementation;
- a real bounded shutdown delivery deadline;
- idempotent sync/async disposal;
- Serilog `SelfLog` diagnostics for persistence/delivery failures.

A normal endpoint outage with healthy local storage does not consume Emergency memory.

The implementation targets `net8.0` and `net10.0`.

## Receiver contract

The matched receiver is `Eigenverft.Service.CentralLogging` at:

```text
POST /api/v1/logs
```

The relay/receiver pair provides at-least-once transport with idempotent receiver storage:

- `EventId` is the stable idempotency key;
- `BatchId` is per-attempt correlation metadata;
- retries with the same event identity/content do not create duplicate stored events.

## Known follow-up

The remaining intentional follow-up areas are:

- bearer authentication for direct remote/external ingestion;
- permanent receiver-rejection isolation/dead-letter behavior;
- a richer health/status surface beyond `SelfLog`;
- optional broader multi-process coordination only if shared-spool operation becomes a real
  requirement.

These are not implied capabilities of the current v1 contract.

## Historical origin

The package preserves the useful durable-first behavior of the discontinued AxonInsight
`SQLiteSinkHttp` implementation. Historical code is background/reference material; current
behavior is defined by the package tests and the repository-level `RELIABILITY.md` and
`RELEASE-READINESS.md` documents.
