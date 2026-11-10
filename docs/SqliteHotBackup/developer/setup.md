# Preparing SqliteHotBackup development

`Eigenverft.NetLib.SqliteHotBackup` is an empty library scaffold in the
Eigenverft.NetLib.Infrastructure repository. Use the .NET 10 SDK to work with
its `.slnx` solution and its `net8.0` / `net10.0` targets. Running both test targets
also requires the matching .NET 8 and .NET 10 runtimes. Restore requires access
to the configured NuGet sources or an adequate local package cache.

## Project ownership

The solution is
`src/sln/Eigenverft.NetLib.SqliteHotBackup/Eigenverft.NetLib.SqliteHotBackup.slnx`.
The library and matching test project are under `src/prj/` with the same names.
They were prepared from the SerilogRelay project setup without its product
code, examples, runtime package dependencies, or source-generation exclusions.

The library retains the project analyzer configuration, XML documentation and
nullable checks, package validation, source-control targets, and build helpers.
`Properties/version.json` supplies independent Nerdbank.GitVersioning with a
`0.1-alpha` base and a path filter for the library directory. NuGet icon,
description, readme, license expression, and release notes are configured under
`Properties/NugetMetadata/` and in the project file.

The test project retains MSTest, method-level parallelization, per-framework
test reports, Coverlet's existing 100% threshold configuration, and NuGet
dependency reports isolated under `NugetReport/<TFM>/` to avoid parallel file
writes. It references the matching library target. There are no
test methods; zero-test execution must not be reported as feature verification.

## Local commands

Run from the repository root:

```powershell
dotnet restore src/sln/Eigenverft.NetLib.SqliteHotBackup/Eigenverft.NetLib.SqliteHotBackup.slnx
dotnet build src/sln/Eigenverft.NetLib.SqliteHotBackup/Eigenverft.NetLib.SqliteHotBackup.slnx --configuration Release --no-restore
dotnet test src/sln/Eigenverft.NetLib.SqliteHotBackup/Eigenverft.NetLib.SqliteHotBackup.slnx --configuration Release --no-build --no-restore
dotnet pack src/prj/Eigenverft.NetLib.SqliteHotBackup/Eigenverft.NetLib.SqliteHotBackup.csproj --configuration Release --no-build --no-restore
```

Builds use each project's ignored `bin/` and `obj/`. Packages go to the library's
`bin/Pack/`. Test diagnostics use the test project's ignored `MSTestResults/`,
`CoverletOutput/`, and `NugetReport/` directories.

## Shared pipeline

`.github/workflows/cicd.ps1` recursively discovers solutions under `src/`; no
new hard-coded workflow entry is required. Both scaffold projects set
`IsCicdEnabled=false`: the pipeline restores and evaluates their properties,
then skips execution and distribution. Local CLI commands remain enabled.

Keep this gate in place while the package is empty. Revisit it when actual
backup implementation and behavioral tests are added and a release is intended.
The legacy `Eigenverft.NetLib.Infrastructure` facade has no reference to the new
library.
