# Getting started

The package is prepared for normal NuGet consumption:

```shell
dotnet add package Eigenverft.NetLib.Infrastructure
```

The current repository establishes the generic infrastructure boundary and the shared Eigenverft public-release baseline before a broader API surface is introduced.

When adding functionality, keep the package boundary narrow:

- Prefer APIs based on the BCL, `Microsoft.Extensions.*`, and Generic Host abstractions.
- Keep ASP.NET Core server concepts such as Kestrel/SNI in WebLib packages.
- Avoid application-specific naming, paths, control-plane concepts, or deployment assumptions in public primitives.
- Add focused tests and XML documentation with every public API.

From the repository root, validate the same basic lifecycle expected by CI:

```shell
dotnet restore src/Eigenverft.NetLib.Infrastructure.slnx
dotnet build src/Eigenverft.NetLib.Infrastructure.slnx --configuration Release
dotnet test src/Eigenverft.NetLib.Infrastructure.slnx --configuration Release
dotnet pack src/prj/Eigenverft.NetLib.Infrastructure/Eigenverft.NetLib.Infrastructure.csproj --configuration Release
```
