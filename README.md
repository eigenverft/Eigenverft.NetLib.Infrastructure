# 🧱 Eigenverft.NetLib.Infrastructure

<!-- Maintenance note: This GitHub README has a NuGet/CommonMark counterpart in README.NUGET.md. Keep shared public-facing content aligned. -->

[![Build Status](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.NetLib.Infrastructure/cicd.yml?branch=main&label=build)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-.NET%208%20%7C%2010-512BD4?logo=dotnet&logoColor=white)](#-target-frameworks) [![License](https://img.shields.io/github/license/eigenverft/Eigenverft.NetLib.Infrastructure?logo=mit)](LICENSE)

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

`Eigenverft.NetLib.Infrastructure` is for infrastructure that is useful outside ASP.NET Core applications and does not inherently depend on web-server concepts.

Examples of suitable future primitives include reusable directory-layout models, Generic Host configuration infrastructure, early-host diagnostics, and other application-neutral building blocks. ASP.NET Core adapters and Kestrel/SNI behavior should stay in `Eigenverft.WebLib.Infrastructure` or another web-specific package.

This keeps dependency direction simple:

```text
Eigenverft.NetLib.Infrastructure
        ▲
        │ optional dependency
        │
Eigenverft.WebLib.Infrastructure
```

## 📦 Installation

Package publication will be enabled when the first public API surface is ready. Once published, installation follows the normal NuGet flow:

```shell
dotnet add package Eigenverft.NetLib.Infrastructure
```

## 🧩 Repository standard

This repository is also the minimal Eigenverft baseline for public .NET libraries:

- `src/prj` / `src/wrk` repository layout
- explicit package and version metadata
- embedded package README and Eigenverft icon
- MIT licensing
- dedicated test project
- Release build/test/pack validation
- dependency, vulnerability, license, SBOM, and DocFX steps in CI/CD
- GitHub Actions entry point in `.github/workflows/cicd.yml`

The CI/CD scripts discover the solution and projects from repository metadata instead of hard-coding this package name. A new library should therefore need repository/package naming and public-facing content changes rather than a bespoke release pipeline.

## 🎯 Target frameworks

The package is prepared to ship dedicated assets for:

- `net8.0`
- `net10.0`

A .NET 9 consumer can use the compatible `net8.0` asset. Preview target frameworks are intentionally excluded from the baseline.

## 🧪 Build and test

From the repository root:

```shell
dotnet build src/Eigenverft.NetLib.Infrastructure.slnx --configuration Release
dotnet test src/Eigenverft.NetLib.Infrastructure.slnx --configuration Release
dotnet pack src/prj/Eigenverft.NetLib.Infrastructure/Eigenverft.NetLib.Infrastructure.csproj --configuration Release
```

## 🚢 Releases

`main` is intended to be the production channel once package publication is enabled. The repository CI/CD pipeline performs build, test, documentation, packaging, dependency-health, license, and release preparation from the same reusable workflow used by other Eigenverft public libraries.

No repository visibility change or NuGet publication is implied by the presence of the release scaffold.

## 🤝 Contributing and support

- 🐛 [Open an issue](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/issues)
- 🔧 [Submit a pull request](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/pulls)

## 📄 License

Licensed under the [MIT License](LICENSE) by Eigenverft.

---

<div align="center">
Made with ❤️ by Eigenverft
</div>
