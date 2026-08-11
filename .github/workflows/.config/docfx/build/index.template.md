---
_layout: landing
---

# {{appName}}

Reusable non-web-specific infrastructure primitives for .NET applications and Generic Host-based services.

## Get started

The package is intentionally small and application-neutral. Generic .NET and Generic Host infrastructure belongs here; ASP.NET Core/Kestrel-specific adapters belong in WebLib packages.

```shell
dotnet add package {{appName}}
```

Continue with the [introduction](docs/introduction.md), the [getting started guide](docs/getting-started.md), or browse the generated API reference.

## Design principles

- General .NET first; web-specific concerns stay outside the core package.
- Small composable primitives rather than an application framework.
- Public APIs must be useful independently of Eigenverft applications.
- Release metadata, documentation, testing, package health, and licensing use the shared Eigenverft public-library pipeline.
