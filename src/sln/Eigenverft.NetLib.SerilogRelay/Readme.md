# Eigenverft.NetLib.SerilogRelay

This folder is the per-library area under `src/sln/` for solution-level or cross-project files that should not sit next to a single `.csproj`.
The `.slnx` lives here; keep the folder while that is true. You can still add extra solution items here.

The `.slnx` and this readme live in this folder. Open a terminal here for the commands below. The CLI finds the one solution in this directory; you do not pass a `.slnx` or `.csproj` path. Other libraries keep their own `.slnx` under `src/sln/<name>/`, so `dotnet` does not ask you to specify a solution.

```text
./                         you are here (this readme + Eigenverft.NetLib.SerilogRelay.slnx)
../../prj/Eigenverft.NetLib.SerilogRelay/    packable class library
../../prj/Eigenverft.NetLib.SerilogRelay.Tests/  tests (not packed)
```

Package metadata, license, icon, and release notes live in `src/prj/Eigenverft.NetLib.SerilogRelay/Properties/NugetMetadata/`.

Project revival and design context is kept here at solution level in [`REVIVAL.md`](REVIVAL.md); it is repository documentation and is intentionally not included in the NuGet package.

The current functional implementation is `../../prj/Eigenverft.NetLib.SerilogRelay/SerilogRelaySink.cs`, migrated from the archived AxonInsight `SQLiteSinkHttp` durable SQLite/HTTP Serilog sink and exposed through the package-aligned `WriteTo.SerilogRelay(...)` API.

`--tl:off` is optional. Without it the CLI shows the compact terminal logger. Add `--tl:off` for the classic per-project log. The commands work either way.

## Restore and build

```bash
dotnet restore
dotnet build
```

## Test

The test project explicitly allows target frameworks to run in parallel. Test results and other per-target-framework reports next to the test project are isolated. No parallelism switch is needed on the command line:

```bash
dotnet test
```

MSTest is explicitly configured for method-level parallel execution within one test assembly. Tests must therefore not share mutable global state.

After a test run, the links below point to generated reports. Each selected target framework writes its own files (`net8.0`, `net10.0`, …).

[Test results (trx)](../../prj/Eigenverft.NetLib.SerilogRelay.Tests/MSTestResults/Eigenverft.NetLib.SerilogRelay.Tests-net10.0.trx)
[Test results (html)](../../prj/Eigenverft.NetLib.SerilogRelay.Tests/MSTestResults/result-net10.0.html)
[Coverlet output](../../prj/Eigenverft.NetLib.SerilogRelay.Tests/CoverletOutput/coverage.net10.0.opencover.xml)

Coverlet measures only the authored class-library code (`[Eigenverft.NetLib.SerilogRelay]*`) and fails `dotnet test` if line, branch, or method coverage is under 100%. Compiler-generated `System.Text.Json` source-generator files under `obj/` are excluded from coverage measurement. The copied `PhysicalMachineBinding` platform helper is also excluded via `[ExcludeFromCodeCoverage]` because its original project has dedicated platform-binding tests; the relay-specific identity persistence and wire integration remain inside the 100% threshold. The migrated sink regression suite currently contains 28 tests; all 28 pass on `net10.0`, where authored library code reaches 100% line, branch, and method coverage.

## Pack

```bash
dotnet pack
```

Creates one `.nupkg` in `src/prj/Eigenverft.NetLib.SerilogRelay/bin/Pack/` containing the library for all selected target frameworks. Test and optional benchmark projects are not packed.

Restore fails this class library on high (`NU1903`) and critical (`NU1904`) vulnerable packages. Low and moderate stay warnings. `NugetReport` next to the tests lists that library's packages (txt/json) and is still info-only.

## Publish

The class library sets `IsPublishable` to `false`. Distribution is `dotnet pack`. To write output to `src/prj/Eigenverft.NetLib.SerilogRelay/bin/Publish/` for the default selected target framework, set `IsPublishable` to `true` and run. The default is defined in `src/prj/Eigenverft.NetLib.SerilogRelay/Properties/Build/SharedProject.props`:

```bash
dotnet publish
```

This is a class library, not an executable.

## CI

Use `-m:1` for the build so a pipeline does not depend on machine load. It avoids occasional file locks when the library is built as a solution project and as a test `ProjectReference` at the same time. Multi-target test execution is already configured as parallel in the test project. Run these commands from this folder so each library has exactly one `.slnx` in the working directory.

```bash
dotnet restore
dotnet build --no-restore -m:1
dotnet test --no-build
dotnet pack
```
