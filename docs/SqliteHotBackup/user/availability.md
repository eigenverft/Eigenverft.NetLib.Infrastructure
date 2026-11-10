# SqliteHotBackup availability

`Eigenverft.NetLib.SqliteHotBackup` is being prepared as an independently
versioned NuGet package in the Eigenverft.NetLib.Infrastructure repository.

The current project has no public API and performs no backups. Online backup,
EF Core database discovery, explicit file registration, ZIP packaging, and
retention are intended capabilities, not available behavior. There are no
installation or application-integration instructions for a working backup
feature yet.

The empty assembly targets .NET 8 and .NET 10. A local package can be generated
to inspect its metadata, but automated distribution is disabled for this
preparation stage. Package creation does not establish that backup behavior
exists or has been tested.
