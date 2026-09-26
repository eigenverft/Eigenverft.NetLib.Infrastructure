# Eigenverft.NetLib.SerilogRelay

This folder contains solution-level files for `Eigenverft.NetLib.SerilogRelay`.

```text
./
  Eigenverft.NetLib.SerilogRelay.slnx
  Readme.md
  RELEASE-READINESS.md
  RELIABILITY.md

../../prj/Eigenverft.NetLib.SerilogRelay/
  packable class library

../../prj/Eigenverft.NetLib.SerilogRelay.Tests/
  tests (not packed)
```

Package metadata, icon, license, and the NuGet-facing README live under:

```text
src/prj/Eigenverft.NetLib.SerilogRelay/Properties/NugetMetadata/
```

Current operating/release scope is documented in
[`RELEASE-READINESS.md`](RELEASE-READINESS.md). Current outage, storage, retry, Emergency,
and shutdown behavior is documented in [`RELIABILITY.md`](RELIABILITY.md).

The implementation is organized by responsibility under
`../../prj/Eigenverft.NetLib.SerilogRelay/`:

```text
Configuration/
Delivery/
Emergency/
Models/
Serialization/
Storage/
SerilogRelaySink.cs
```

## Restore and build

Run from this solution directory:

```bash
dotnet restore
dotnet build
```

## Test

```bash
dotnet test
```

The test project supports multi-target execution. MSTest is configured for method-level
parallel execution, so tests must not share mutable global state.

Generated net10 reports are available at:

[Test results (trx)](../../prj/Eigenverft.NetLib.SerilogRelay.Tests/MSTestResults/Eigenverft.NetLib.SerilogRelay.Tests-net10.0.trx)

[Test results (html)](../../prj/Eigenverft.NetLib.SerilogRelay.Tests/MSTestResults/result-net10.0.html)

[Coverlet output](../../prj/Eigenverft.NetLib.SerilogRelay.Tests/CoverletOutput/coverage.net10.0.opencover.xml)

Coverlet measures authored class-library code and enforces 100% line, branch, and method
coverage. Compiler-generated `System.Text.Json` source-generator files are excluded. The
copied `PhysicalMachineBinding` platform helper is also excluded because its original project
has dedicated platform-binding tests; relay-specific identity persistence/wire behavior remains
inside the coverage gate.

## Pack

```bash
dotnet pack
```

The package output is written under
`src/prj/Eigenverft.NetLib.SerilogRelay/bin/Pack/` and contains the library for the selected
target frameworks.

Restore fails on high/critical vulnerable package findings (`NU1903` / `NU1904`).

## CI

Use `-m:1` for the build to avoid occasional duplicate-output file locks when the solution
library and test `ProjectReference` are built together:

```bash
dotnet restore
dotnet build --no-restore -m:1
dotnet test --no-build
dotnet pack
```

Before release, also follow the final validation gates in
[`RELEASE-READINESS.md`](RELEASE-READINESS.md).
