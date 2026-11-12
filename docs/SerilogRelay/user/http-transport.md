# SerilogRelay HTTP transport

The sender's built-in HTTP clients use Happy Eyeballs when opening a TCP
connection. No additional sink configuration is required. This helps a desktop
application reach the receiver when its network advertises IPv6 but cannot
complete an IPv6 connection, or when the equivalent problem affects IPv4.

After DNS resolution, the sender preserves the resolver's preferred address family and alternates
IPv6 and IPv4 addresses, preserving their order within each family and removing
duplicates. IPv4-mapped IPv6 addresses are normalized to IPv4. The first connection
starts immediately. A pending connection gets
a 250 ms head start before the next address is tried; a failed connection allows
the next address to start immediately. The first successful TCP connection is
used, and all other attempts are canceled and their connections closed.

Only TCP connection establishment is raced. The HTTP log request is sent over
the winning connection. This does not introduce parallel POSTs or extra HTTP
retries. Connection pooling, normal TLS certificate validation, the original
hostname for TLS and HTTP, and the existing delivery acknowledgment rules remain
in effect. `dangerousAcceptAnyServerCertificate` still requires an explicit
opt-in and should be reserved for controlled testing.

`Delivery.RequestTimeout` remains the entire HTTP request budget (two seconds
by default), not a timeout for each address. A working alternate connection
still needs enough time for TLS, request delivery and the receiver's response.
Happy Eyeballs does not recover from a TLS error or an unhealthy application
behind an otherwise successful TCP connection. Failed delivery stays eligible
for the existing durable-spool retry policy, subject to its configured limits.

The .NET 8 and .NET 10 assets use the runtime's `SocketsHttpHandler`. The
`netstandard2.1` asset uses `StandardSocketsHttpHandler` 2.2.0.10 with the same
connection-selection code; it adds no native HTTP library. The backport does
not discover OS proxies itself, so the sender supplies the system proxy returned
by `WebRequest.GetSystemWebProxy()`. Hosts must still support TCP sockets,
DNS, TLS and the native SQLite library. A real Unity Mono/IL2CPP player needs
platform-specific validation; CLR tests do not establish that deployment support.
The Standard DNS API cannot cancel an OS lookup already in progress; the sender
stops awaiting it when canceled.

If you supply `SerilogRelayOptions.HttpClient`, that client owns transport
selection, proxies, certificate policy and Happy Eyeballs behavior. The sender
does not replace its handler or dispose the supplied client. Keep it alive for
the application's lifetime and avoid adding unbounded retries: the sender
already retries failed deliveries.
