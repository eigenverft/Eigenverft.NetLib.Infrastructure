# 🧱 Eigenverft.NetLib.Infrastructure

[![Targets](https://img.shields.io/badge/targets-.NET%208%20%7C%2010-512BD4?logo=dotnet&logoColor=white)](#-target-frameworks) [![License](https://img.shields.io/github/license/eigenverft/Eigenverft.NetLib.Infrastructure?logo=mit)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)

Reusable non-web-specific infrastructure primitives for .NET applications and Generic Host-based services.

The package is intentionally the generic counterpart to web-specific infrastructure: reusable .NET and hosting concerns belong here, while ASP.NET Core/Kestrel-specific adapters remain in dedicated WebLib packages.

---

## ✨ At a glance

| | |
| --- | --- |
| Package | `Eigenverft.NetLib.Infrastructure` |
| Target frameworks | .NET 8 and .NET 10 |
| Scope | General .NET and Generic Host infrastructure |
| Web-specific APIs | Intentionally excluded from the core package |
| License | MIT |

## 🎯 Design boundary

The package is for infrastructure that is useful outside ASP.NET Core applications and does not inherently depend on web-server concepts.

The intended dependency direction is simple: generic primitives live in `Eigenverft.NetLib.Infrastructure`; ASP.NET Core adapters may build on them from `Eigenverft.WebLib.Infrastructure` or another web-specific package.

## 📦 Installation

```shell
dotnet add package Eigenverft.NetLib.Infrastructure
```

Or with the NuGet Package Manager:

```powershell
Install-Package Eigenverft.NetLib.Infrastructure
```

## 🎯 Target frameworks

The package ships dedicated assets for:

- `net8.0`
- `net10.0`

A .NET 9 consumer can use the compatible `net8.0` asset. Preview target frameworks are intentionally excluded.

## 🔗 Project links

- [GitHub repository](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure)
- [Issues](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/issues)
- [NuGet package](https://www.nuget.org/packages/Eigenverft.NetLib.Infrastructure)

## 📄 License

Licensed under the [MIT License](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE) by Eigenverft.

---

Made with ❤️ by Eigenverft
