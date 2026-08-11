---
_layout: landing
---

# Eigenverft.NetLib.Infrastructure

Small, reusable infrastructure primitives for .NET applications and Generic Host-based services.

## Get started

```shell
dotnet add package Eigenverft.NetLib.Infrastructure
```

```csharp
using Eigenverft.NetLib.Infrastructure.Hosting.DirectoryLayout;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.AddDefaultDirectoryLayout();

IAppDirectoryLayout directories = builder.GetDirectoryLayout();
```

The current package provides executable-rooted application directories with automatic creation and writable validation. The default set is `AppLogs`, `AppData`, `AppState`, `AppCerts`, and `AppSettings`.

Continue with the [introduction](docs/introduction.md), the [getting started guide](docs/getting-started.md), or browse the generated API reference.

## Design principles

- Small, composable primitives rather than an application framework.
- Generic Host integration where it improves normal application startup.
- Predictable operational behavior and early validation.
- Keeps the package focused on reusable .NET and Generic Host infrastructure.
