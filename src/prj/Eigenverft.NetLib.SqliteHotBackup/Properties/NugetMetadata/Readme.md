# Eigenverft.NetLib.SqliteHotBackup

Preparation scaffold for a future SQLite hot-backup package for .NET applications.

**Status: no backup implementation, public API, or behavioral tests are included.**
The current assembly is empty apart from build and test-access metadata. It cannot
create, discover, archive, restore, or retain database backups.

The project targets .NET 8 and .NET 10 and is maintained as an independently
versioned package in the Eigenverft.NetLib.Infrastructure repository. Its
versioning configuration uses the collection's `1.0.0` base and shared
release-reference rules. Build and distribution follow the existing package
configuration.

The intended direction is to extract the existing GlobalServer/PWA backup
behavior, preserving online backup, database discovery, orchestration, and
optional packaging while keeping scheduler integrations optional. These are
design goals rather than available features. No SQLite, EF Core, Hangfire,
ASP.NET Core, or Serilog runtime dependencies have been selected for this project.

See the [product documentation](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/docs/SqliteHotBackup/product/overview.md)
for the design context and the [developer guide](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/docs/SqliteHotBackup/developer/setup.md)
for the prepared project structure and local commands.

Licensed under the MIT License by Eigenverft.
