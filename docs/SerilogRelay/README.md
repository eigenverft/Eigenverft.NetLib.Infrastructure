# SerilogRelay sender documentation

SerilogRelay forwards Serilog events over HTTP with a bounded durable SQLite
spool. These pages cover the sender's network transport and its verification.

- [Using the HTTP transport](user/http-transport.md)
- [Diagnosing network delivery](operations/network-delivery.md)
- [Connection selection and delivery guarantees](product/connection-selection.md)
- [Compatibility and adoption status](management/compatibility-status.md)
- [Building and testing](developer/testing.md)

The [package README](../../src/prj/Eigenverft.NetLib.SerilogRelay/Properties/NugetMetadata/Readme.md)
contains installation, sink configuration and spool settings.
