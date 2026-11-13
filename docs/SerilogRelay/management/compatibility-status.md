# SerilogRelay transport compatibility

The compatible ingestion-key naming increment introduces IngestionKey while
retaining the existing bearerToken parameter without obsolete warnings. HTTP
Bearer transport, payloads and spool schema are unchanged. Documentation names
the submission purpose and distinguishes a recoverable client-distributed key
from private query/admin credentials and trusted producer identity. The naming
does not require a server-only key to be public or promise that it is secret.
All 976 sender test executions pass at 100% coverage; package/API validation
against 1.0.0.19 and old/new receiver interoperability checks pass. These are
local candidate results; the public package metadata changes only on a new release.

The sender source targets .NET Standard 2.1, .NET 8 and .NET 10 with the same
public sink API, persisted spool schema and HTTP payload. Built-in clients use
Happy Eyeballs to reduce failures caused by an unusable IPv4 or IPv6 route.
The 250 ms connection head start follows RFC 8305's recommended default and
leaves most of the default two-second HTTP budget for delivery.

The Standard asset alone adds StandardSocketsHttpHandler 2.2.0.10. This existing
managed HTTP implementation supplies the connection callback missing from the
Standard API and avoids maintaining a custom HTTP/TLS stack or adding native
HTTP binaries. Dedicated .NET 8/10 assets continue to use the runtime handler.
Normal certificate validation remains enabled; callers providing their own
HttpClient retain control of its transport policy.

Repository verification runs the native and Standard assemblies on .NET 8/10,
including controlled connection races, real IPv4/IPv6 sockets and local TLS.
Those CLR checks do not establish Unity Mono/IL2CPP player compatibility or
native SQLite deployment support. Consumer adoption requires an available
package from the existing publication workflow and a check on the actual player
platform. No receiver change is required for this sender capability.

On 2026-11-13, all four native/Standard test matrices passed: 936 successful
executions at 100% line, branch and method coverage. Local package/API validation
against published 1.0.0.15 passed and the transitive audit reported no known
vulnerable entries. These results describe local source/artifact verification,
not public-feed availability or a Unity player certification.
