# Eigenverft.NetLib.SqliteHotBackup

This solution prepares an independently versioned SQLite hot-backup package in
the Eigenverft.NetLib.Infrastructure repository. It currently contains no backup
implementation, public API, or behavioral tests.

| Project | Responsibility |
| --- | --- |
| `../../prj/Eigenverft.NetLib.SqliteHotBackup/` | Empty packable library for .NET 8 and .NET 10, with analyzer rules, versioning, build helpers, and NuGet metadata. |
| `../../prj/Eigenverft.NetLib.SqliteHotBackup.Tests/` | Matching MSTest project with coverage and package-report configuration, ready for future tests. |

The common project setup was copied from SerilogRelay. Its implementation,
examples, runtime dependencies, and feature-specific test settings were omitted.
The library version starts at `0.1-alpha`. Both new projects set
`IsCicdEnabled=false`, so recursive solution discovery finds them but the shared
pipeline skips their build and distribution stages. Local builds and packing
remain available.

From the repository root:

```powershell
dotnet restore src/sln/Eigenverft.NetLib.SqliteHotBackup/Eigenverft.NetLib.SqliteHotBackup.slnx
dotnet build src/sln/Eigenverft.NetLib.SqliteHotBackup/Eigenverft.NetLib.SqliteHotBackup.slnx --configuration Release --no-restore
dotnet test src/sln/Eigenverft.NetLib.SqliteHotBackup/Eigenverft.NetLib.SqliteHotBackup.slnx --configuration Release --no-build --no-restore
dotnet pack src/prj/Eigenverft.NetLib.SqliteHotBackup/Eigenverft.NetLib.SqliteHotBackup.csproj --configuration Release --no-build --no-restore
```

The test command currently discovers zero tests and provides no evidence of
backup correctness. A generated NuGet package contains only the empty assembly
and package metadata.

See the [developer guide](../../../docs/SqliteHotBackup/developer/setup.md),
[product design context](../../../docs/SqliteHotBackup/product/overview.md), and
[current status](../../../docs/SqliteHotBackup/management/status.md).
