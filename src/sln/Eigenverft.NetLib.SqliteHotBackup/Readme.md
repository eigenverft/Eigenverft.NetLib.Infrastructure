# Eigenverft.NetLib.SqliteHotBackup

One independently versioned SQLite online-backup package for .NET 8 and .NET 10.
It provides DI registration, lazy EF discovery, explicit file targets, stable IDs,
all/single/selected-target operations, progress-stall limits, safe publication,
best-effort result aggregation, optional ZIP packaging and shutdown checkpoints.
Scheduling and scheduler retries remain with the consumer.

| Project | Responsibility |
| --- | --- |
| `../../prj/Eigenverft.NetLib.SqliteHotBackup/` | Packable library with matching-generation EF Core SQLite dependencies. |
| `../../prj/Eigenverft.NetLib.SqliteHotBackup.Tests/` | MSTest coverage of contracts and synthetic SQLite integrations on both frameworks. |

From the repository root:

```powershell
dotnet restore src/sln/Eigenverft.NetLib.SqliteHotBackup/Eigenverft.NetLib.SqliteHotBackup.slnx
dotnet build src/sln/Eigenverft.NetLib.SqliteHotBackup/Eigenverft.NetLib.SqliteHotBackup.slnx --configuration Release --no-restore -m:1
dotnet test src/sln/Eigenverft.NetLib.SqliteHotBackup/Eigenverft.NetLib.SqliteHotBackup.slnx --configuration Release --no-build --no-restore
dotnet pack src/prj/Eigenverft.NetLib.SqliteHotBackup/Eigenverft.NetLib.SqliteHotBackup.csproj --configuration Release --no-build --no-restore
```

Use `-m:1` for builds because framework builds share dependency-report filenames.
Outputs stay in ignored project `bin/`, `obj/`, `MSTestResults/`, `CoverletOutput/`
and `NugetReport/` folders. Packages go to library `bin/Pack/`. Shared CI discovers
this solution recursively; the Infrastructure compatibility aggregate does not
reference this package. Local packing does not publish a release.

See [consumer integration](../../../docs/SqliteHotBackup/user/availability.md),
[developer setup](../../../docs/SqliteHotBackup/developer/setup.md),
[execution limits](../../../docs/SqliteHotBackup/user/execution-timing.md), and
[delivery status](../../../docs/SqliteHotBackup/management/status.md).
