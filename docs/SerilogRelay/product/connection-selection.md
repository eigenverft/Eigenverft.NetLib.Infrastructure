# SerilogRelay connection selection

The sender needs to deliver desktop application logs from networks where one
advertised address family may be unusable. Its built-in HTTP clients therefore
race TCP connections using a 250 ms Happy Eyeballs head start. Resolver
preference and address order within each family are retained; families are
interleaved and duplicate addresses removed. An immediate failure accelerates
the next attempt. The first successful connection wins and the other
connections are closed, including a connection that succeeds after cancellation.

This is a connection-selection policy, not a second delivery policy. It runs
after the platform resolver returns its address list. It does not race DNS
answers, HTTP POSTs, TLS handshakes or receiver acknowledgments. The original
hostname remains the TLS and HTTP identity. Existing request deadlines, endpoint
backoff, durable storage limits and the completed-204 acknowledgment contract
continue to govern delivery. A successful TCP connection does not establish
that the receiver application is healthy.

All three sender assets use the same selector. .NET 8 and .NET 10 use the
runtime's SocketsHttpHandler; Standard 2.1 uses the compatible
StandardSocketsHttpHandler 2.2.0.10 because the Standard API lacks a connection
callback. A caller-supplied HttpClient keeps its own transport policy. Receiver
code and the persisted spool/wire formats do not need changes.
