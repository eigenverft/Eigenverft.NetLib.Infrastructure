# Introduction

`Eigenverft.NetLib.Infrastructure` provides small, reusable infrastructure primitives for .NET applications and Generic Host-based services.

The first public primitive is an executable-rooted application directory layout. It gives applications predictable locations for logs, data, state, certificates, and settings without depending on the process working directory.

The normal integration is intentionally small:

```csharp
builder.AddDefaultDirectoryLayout();
IAppDirectoryLayout directories = builder.GetDirectoryLayout();
```

Mapped directories are created and checked for write access during registration. The same layout instance remains available through dependency injection after the host is built.

The package stays application-neutral and focuses on reusable .NET and Generic Host infrastructure that can be adopted independently.
