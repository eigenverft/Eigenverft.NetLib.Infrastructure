# Eigenverft.NetLib.Networking

The `.slnx` and this readme live in this folder. Open a terminal here for the commands below. The CLI finds the one solution in this directory; you do not pass a `.slnx` or `.csproj` path. Other libraries keep their own `.slnx` under `src/sln/<name>/`, so `dotnet` does not ask you to specify a solution.

```text
./                         you are here (this readme + Eigenverft.NetLib.Networking.slnx)
../../prj/Eigenverft.NetLib.Networking/    packable class library
../../prj/Eigenverft.NetLib.Networking.Tests/  tests (not packed)
```

Package metadata, license, icon, and release notes live in `src/prj/Eigenverft.NetLib.Networking/NugetAssets/`.

`--tl:off` is optional. Without it the CLI shows the compact terminal logger. Add `--tl:off` for the classic per-project log. The commands work either way.

## Restore and build

```bash
dotnet restore
dotnet build
```

## Test

Run the tests for all target frameworks:

```bash
dotnet test
```

## Pack

```bash
dotnet pack
```

Creates one `.nupkg` in `src/prj/Eigenverft.NetLib.Networking/bin/Pack/` containing the library for all selected target frameworks. Test and optional benchmark projects are not packed.

Optional: copy the package to a local feed by setting `LocalPackagesDir` in the library project, or:

```bash
dotnet pack -p:LocalPackagesDir="path/to/local/packages"
```

## Publish

```bash
dotnet publish
```

Writes library output to `src/prj/Eigenverft.NetLib.Networking/bin/Publish/` for the highest selected target framework. This is a class library, not an executable.

## CI

Use `-m:1` for the build so a pipeline does not depend on machine load. It avoids occasional file locks when the library is built as a solution project and as a test `ProjectReference` at the same time. Run these commands from this folder so each library has exactly one `.slnx` in the working directory.

```bash
dotnet restore
dotnet build --no-restore -m:1
dotnet test --no-build
dotnet pack
```
