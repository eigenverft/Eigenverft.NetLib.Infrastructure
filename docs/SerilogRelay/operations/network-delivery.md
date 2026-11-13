# Diagnosing SerilogRelay network delivery

`SerilogRelayOptions.IngestionKey` is the preferred submission-key setting; the
old `bearerToken:` argument still works. Use the raw value without a Bearer
prefix. A null option falls back to the legacy argument; an explicit blank
suppresses the sink's header. Different nonblank values fail setup instead of
silently selecting one. A custom HTTP client can still add its own authorization.
Create/reconfigure the sink to capture a rotated value. Do not treat a key shipped
with an application as a private query credential or trusted sender identity;
use HTTPS and separate private authorization for read/admin endpoints.

The sender's built-in clients use Happy Eyeballs for TCP connections. After DNS
resolution, they alternate IPv6/IPv4 addresses in the resolver's preferred order,
starting another attempt after 250 ms while the preceding attempt is pending.
Connection failures allow an immediate next attempt. One successful connection
is retained and other attempts are canceled and closed. This helps clients on
networks with an unusable address family without globally disabling IPv6.

This does not make every endpoint failure recoverable within a request. DNS
resolution, TLS negotiation, HTTP upload and response processing still consume
the request budget. `Delivery.RequestTimeout` defaults to two seconds for the
entire request and must stay below the 30-second spool claim lease. Happy
Eyeballs does not select another server after TCP has succeeded but TLS or the
receiver application fails. Unacknowledged events follow the existing retry
and bounded-spool policies; at capacity or expiry, configured retention rules
still apply.

When delivery fails, check name resolution, usable receiver addresses, proxy
requirements, certificate hostname/trust and the receiver's HTTP response.
Only a completed HTTP 204 acknowledges a batch. Retry-After and endpoint
backoff remain separate from TCP connection selection. Do not disable
certificate validation to diagnose a TCP routing problem.

The .NET 8/10 assets use their runtime HTTP handler. The Standard asset uses
`StandardSocketsHttpHandler` 2.2.0.10 and receives the system proxy explicitly;
it adds no native HTTP binaries. Standard DNS cancellation stops awaiting a
lookup but cannot stop the underlying OS lookup. A supplied
`SerilogRelayOptions.HttpClient` controls its own transport, proxy and
certificate policy, including whether Happy Eyeballs is available. Unity
deployments need a real Mono/IL2CPP player and native SQLite check.
