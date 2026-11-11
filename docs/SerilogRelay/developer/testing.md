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

The runtime reliability contract, including spool ownership, delivery, shutdown
and receiver behavior, is documented in
[the reliability guide](../../../src/sln/Eigenverft.NetLib.SerilogRelay/RELIABILITY.md).

Verified on 2026-11-11: the CI-equivalent full run passes 205 tests on each of
.NET 8 and .NET 10 with 100% line, branch and method coverage. The runtime assembly
and coverage configuration were unchanged by this correction.
