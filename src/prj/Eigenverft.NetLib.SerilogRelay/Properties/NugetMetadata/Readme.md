# Eigenverft.NetLib.SerilogRelay

Durable Serilog relay for forwarding application logs to Eigenverft CentralLogging while keeping a local persistent spool.

## Revival origin

This package continues the sender-side logging revival originally scaffolded as `Eigenverft.NetLib.SerilogCentralLoggingSink`.

The project revives the sender-side logging code from the discontinued AxonInsight infrastructure.

Primary historical references:

`personal-archive-issues/Discontinued/AxonInsight/src/AxonInsight.Client/AxonInsight.Installer/AxonInsight.Installer.Projects/AxonInsight.Library/Extensions/LoggerSinkConfigurationExtensions/SQLiteSinkHttp.cs`

and the corresponding application call sites such as:

`personal-archive-issues/Discontinued/AxonInsight/src/AxonInsight.Client/AxonInsight.Installer/AxonInsight.Installer.Projects/AxonInsight.Initializer/Program.cs`

The old `SQLiteSinkHttp` implemented the important behavior directly inside the Serilog sink: each event was persisted locally to SQLite, unsent rows survived outages/restarts, a background sender batched pending rows to an HTTP endpoint, and rows were marked sent after successful delivery.

The matching receiver is being revived separately as:

`Eigenverft.Service.CentralLogging`

## Direction

The intended sender experience remains deliberately small: install the relay and point it at a target. Application code should not need to understand the CentralLogging wire protocol, batching, retry loop, spool schema, or server storage.

The revival should preserve:

- durable local persistence before network delivery;
- restart-safe pending delivery;
- independent background batching/retry;
- simple `.WriteTo.CentralLogging(...)` configuration;
- localhost and private-infrastructure use as first-class cases.

It should improve:

- explicit certificate-validation modes instead of globally accepting any certificate;
- optional bearer-token authentication;
- stable event IDs and idempotent delivery with the service;
- observable spool/failure state rather than silent loss;
- configurable retention, batching, storage location, and transport behavior.

This is the reusable library side of the revival and is the likely candidate for public packaging later if the project is opened.

See `docs/REVIVAL.md` for the initial preservation and redesign notes.
