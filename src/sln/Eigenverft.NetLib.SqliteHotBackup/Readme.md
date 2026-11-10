# Eigenverft.NetLib.SqliteHotBackup

This solution prepares an independently versioned SQLite hot-backup package in
the Eigenverft.NetLib.Infrastructure repository. It currently contains no backup
implementation, public API, or behavioral tests.

| Project | Responsibility |
| --- | --- |
| `../../prj/Eigenverft.NetLib.SqliteHotBackup/` | Empty packable library for .NET 8 and .NET 10, with analyzer rules, versioning, build helpers, and NuGet metadata. |
| `../../prj/Eigenverft.NetLib.SqliteHotBackup.Tests/` | Matching MSTest project with coverage and package-report configuration, ready for future tests. |

The common project setup was copied from SerilogRelay. Its implementation,
examples, and runtime dependencies were omitted. The library uses the same
`1.0.0` version base, release-reference rules, and default CI activation as the
existing active packages. Recursive solution discovery includes this solution
in the shared build and distribution pipeline.

From the repository root:

```powershell
dotnet restore src/sln/Eigenverft.NetLib.SqliteHotBackup/Eigenverft.NetLib.SqliteHotBackup.slnx
dotnet build src/sln/Eigenverft.NetLib.SqliteHotBackup/Eigenverft.NetLib.SqliteHotBackup.slnx --configuration Release --no-restore -m:1
dotnet test src/sln/Eigenverft.NetLib.SqliteHotBackup/Eigenverft.NetLib.SqliteHotBackup.slnx --configuration Release --no-build --no-restore
dotnet pack src/prj/Eigenverft.NetLib.SqliteHotBackup/Eigenverft.NetLib.SqliteHotBackup.csproj --configuration Release --no-build --no-restore
```

The test command currently discovers zero tests and provides no evidence of
backup correctness. A generated NuGet package contains only the empty assembly
and package metadata.

The build uses `-m:1`, as repository CI does, because the copied test template
writes both frameworks' NuGet reports to the same directory.

See the [developer guide](../../../docs/SqliteHotBackup/developer/setup.md),
[product design context](../../../docs/SqliteHotBackup/product/overview.md), and
[current status](../../../docs/SqliteHotBackup/management/status.md).
