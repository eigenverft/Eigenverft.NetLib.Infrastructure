# Introduction

`Eigenverft.NetLib.Infrastructure` is the application-neutral infrastructure layer for reusable .NET and Generic Host building blocks.

The package boundary is intentional: functionality belongs here when it is useful outside ASP.NET Core and does not inherently depend on web-server abstractions. Web-specific adapters such as Kestrel/SNI integration remain in WebLib packages and may depend on this package where useful.

The public API surface is expected to grow deliberately from proven infrastructure that already exists in Eigenverft applications and libraries. Extraction should preserve useful behavior while removing application-specific assumptions.

The repository itself also serves as the minimal public-library release baseline: package metadata, tests, documentation, dependency health, license checks, SBOM generation, NuGet packaging, and GitHub Actions are prepared consistently so new public libraries can reuse the same structure with minimal repository-specific changes.
