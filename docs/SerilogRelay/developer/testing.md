# Verifying SerilogRelay

Use the .NET 10 SDK and the .NET 8/.NET 10 runtimes. From the repository root:

```powershell
dotnet test src/prj/Eigenverft.NetLib.SerilogRelay.Tests/Eigenverft.NetLib.SerilogRelay.Tests.csproj --configuration Release -m:1 -p:Stage=test -p:Platform=AnyCPU -p:Deterministic=true -p:UseSharedCompilation=false -p:ContinuousIntegrationBuild=false
```

This matches the shared CI test invocation for both target frameworks. The suite
requires 100% line, branch and method coverage of the library. TRX/HTML reports
are in the test project's ignored `MSTestResults/`; per-framework coverage files
are in `CoverletOutput/`. Coverage is a separate gate: all test assertions can pass
while an unexecuted branch still fails the command. For a focused test, add
`--framework net8.0 --filter FullyQualifiedName~<method>`; its partial coverage is
not expected to satisfy the full-suite gate.

The sender library also targets `netstandard2.1`. That target is a library asset,
not a test runtime. Run the same suite against it on both supported runtimes:

```powershell
dotnet test src/prj/Eigenverft.NetLib.SerilogRelay.NetStandard.Tests/Eigenverft.NetLib.SerilogRelay.NetStandard.Tests.csproj --configuration Release -m:1
```

The separate test project links the existing test sources, forces its reference
to the Standard assembly and includes that asset's extra runtime dependencies.
It is included in the sender solution and the existing CI project discovery;
no shared workflow change is required. Its own bin/obj, MSTestResults and
CoverletOutput directories isolate reports from the native-asset test project.
`LibraryTargetsExpectedFramework` verifies the loaded assembly's
TargetFrameworkAttribute so a native-asset run cannot stand in for this check.
The 100% coverage gate applies to this assembly too.

Compatibility tests preserve SHA-256 identifiers, process metadata and stream
read semantics. The full reliability suite also exercises request/shutdown
deadlines, bounded response copying, replay and storage recovery through the
Standard implementation. These CLR runs do not establish Unity player support;
native SQLite distribution and Mono/IL2CPP behavior need a real player check.

`SerilogRelayHappyEyeballsTests` protects TCP address selection independently
of HTTP delivery. Controlled resolver/connect/delay tasks check the 250 ms
head start without wall-clock sleeps, alternating families, immediate failure,
an earlier attempt winning after later failures, cancellation and late successful
losers. Losing connections must close even if they complete after the caller
has already received its winner or cancellation. The same linked tests run
against every asset.

Loopback tests exercise real IPv4, IPv6 and IPv4-mapped IPv6 addresses. HTTPS tests verify that the
built-in handlers preserve the original TLS hostname, reject an untrusted server
certificate by default and accept it only with the existing dangerous opt-in.
The local TLS fixture imports its generated PFX because Windows Schannel cannot
use the generated ephemeral private key directly. It does not change machine
trust. No production receiver or public log endpoint is used.

The corrupted-spool fixture explicitly awaits the disabled spool's maintenance
worker before disposing its sink. This verifies that maintenance stops without
depending on disposal cancellation to end it, and keeps the existing 100% gate
independent of whether that worker happened to run earlier in another test.

The Standard asset alone depends on `StandardSocketsHttpHandler` 2.2.0.10.
It provides the HTTP connection callback missing from the Standard API; the
sender supplies its system proxy explicitly. The connector races only TCP
establishment and returns an owned stream to the handler, leaving HTTP, TLS,
pooling and redirects there. Custom clients bypass this built-in transport.

Background-worker startup must be tested independently of thread scheduling.
Shutdown can cancel a token before a queued worker starts, or while a running
worker is waiting. `SpoolMaintenanceStopsWhenCancellationPrecedesWorkerStart`
explicitly supplies the first state after stopping the existing workers, invokes
maintenance with its already-canceled token, bounds completion and verifies the
pending event remains. Other lifecycle tests cover cancellation of active waits.

Without that explicit case, the maintenance loop's initial canceled condition was
covered only when background-task scheduling happened to produce it elsewhere.
CI and an equivalent local run reproduced 204 passing tests with 99.95% line and
99.88% branch coverage on .NET 8. Preserve the deterministic lifecycle case and the
coverage gate; do not substitute longer sleeps or coverage exclusions.

`ShutdownRetriesHangingResponsesAtOneSecondIntervals` checks durable and emergency
delivery over TCP: two unacknowledged requests time out, the third is held behind
an explicit response gate, and shutdown completes only after its acknowledgment.
It uses a 30-second test budget while retaining one-second request/retry settings.
The existing manual time provider drives the two HTTP cancellations; the
successful response is not exposed to a racing real-time request deadline.
Real retry spacing is still measured, and HTTP timers must be gone after cleanup.
The original three-second overall budget left insufficient margin for two full
request timeouts plus TCP and SQLite acknowledgment on a loaded CI runner.
`ShutdownCancelsInFlightFlushAtDeadline` separately verifies the default
three-second deadline and retention of the unsent row. The deterministic
`ShutdownRetryDoesNotWaitAgainWhenRequestConsumedItsInterval` check protects
against adding an extra retry interval after a timed-out request; it uses an
already elapsed monotonic timestamp and requires no sleep.

`PayloadTargetSplitsEscapedJsonAndDrainsEveryEvent` inspects the actual serialized
HTTP request bodies through the existing recording message handler. It checks
six batches of two escaped messages, the byte limit, ordering/counts and an empty
durable spool for both normal and shutdown delivery. The fixture uses a 30-second
test shutdown budget and waits for acknowledgments before inspecting the recorded
bodies. Its purpose is payload splitting, while separate TCP tests protect HTTP
response completion and the default three-second shutdown deadline.

The former six-request TCP fixture also required complete draining inside that
default deadline. A controlled delayed-response run reproduced one passing normal
case and one shutdown timeout on .NET 10. Keep payload assertions independent of
network scheduling and production deadline timing; retain the separate deadline
and backlog-retention tests.

The runtime reliability contract, including spool ownership, delivery, shutdown
and receiver behavior, is documented in
[the reliability guide](../../../src/sln/Eigenverft.NetLib.SerilogRelay/RELIABILITY.md).

Verified on 2026-11-11: the CI-equivalent full run passes 206 tests on each of
.NET 8 and .NET 10 with 100% line, branch and method coverage. The runtime assembly
and coverage configuration were unchanged by this correction.

Verified on 2026-11-13: the native-asset test project passes 209 tests on each
runtime, and the Standard-asset project passes 211 on each runtime: 840 successful
executions with 100% line, branch and method coverage in all four matrices.
Package validation checks the three framework assets and compatibility with the
previous published package. No Unity editor or player execution is claimed.

Verified on 2026-11-13 for Happy Eyeballs: native assemblies pass 233 tests per
runtime and the Standard assembly passes 235 per runtime (936 executions), with
100% line, branch and method coverage in all four matrices. Package/API validation
against published 1.0.0.15 succeeds, and the transitive dependency audit reports
no known vulnerable entries. Real Unity player validation remains separate.
