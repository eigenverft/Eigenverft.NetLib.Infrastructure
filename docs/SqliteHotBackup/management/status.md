# SqliteHotBackup development status

`Eigenverft.NetLib.SqliteHotBackup` is prepared as a separate package project in
the Eigenverft.NetLib.Infrastructure collection. This placement follows the
existing repository model of independently versioned capabilities with shared
development and delivery tooling.

The delivered preparation consists of a .NET 8/.NET 10 library and test project,
a dedicated solution, analyzer/build configuration, independent
versioning, and NuGet metadata. There is no usable backup functionality and no
behavioral test evidence yet. Versioning uses the same `1.0.0` base and
release-reference rules as the existing active packages. Both projects follow
the normal shared CI configuration.

Local verification completed restore, Release builds for both targets, and
NuGet package generation. Package inspection confirmed both target assemblies
and no public API types or runtime package dependencies. The test runner starts
for both frameworks and discovers zero tests; this verifies the prepared tooling,
not backup behavior.

The existing GlobalServer/PWA backup code is the intended extraction source
according to the original project design. It has not yet been compared or
extracted here. The next substantive work requires assessing those copies and
agreeing the first implementation scope. No delivery date, release guarantee,
or final integration/package split has been established.
