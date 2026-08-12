# 🧱 Eigenverft.NetLib.Infrastructure

<!-- Maintenance note: This GitHub README has a NuGet/CommonMark counterpart in README.NUGET.md. Keep shared public-facing content aligned. -->

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.NetLib.Infrastructure?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Infrastructure) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.NetLib.Infrastructure?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Infrastructure) [![Build Status](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.NetLib.Infrastructure/cicd.yml?branch=main&label=build)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-.NET%208%20%7C%2010-512BD4?logo=dotnet&logoColor=white)](#-target-frameworks) [![License](https://img.shields.io/github/license/eigenverft/Eigenverft.NetLib.Infrastructure?logo=mit)](LICENSE)

Small, reusable infrastructure primitives for .NET applications and Generic Host-based services.

Provides predictable, executable-rooted application directories with automatic creation and writable validation.

Also includes generic reversible string transforms, JSON-safe Base92 representation, machine binding, DPAPI machine-scope transforms, and certificate primitives.

---

## ✨ At a glance

| | |
| --- | --- |
| Package | `Eigenverft.NetLib.Infrastructure` |
| Primary API | `HostApplicationBuilderFactory.CreateWithDefaultDirectory()` |
| Root | `AppContext.BaseDirectory` |
| Default folders | `AppLogs`, `AppData`, `AppState`, `AppCerts`, `AppSettings` |
| Host integration | Available before `Build()` and through DI afterwards |
| Target frameworks | .NET 8 and .NET 10 |
| License | MIT |

## 📦 Installation

```shell
dotnet add package Eigenverft.NetLib.Infrastructure
```

Or with the NuGet Package Manager:

```powershell
Install-Package Eigenverft.NetLib.Infrastructure
```

## 🚀 Quick start

```csharp
using Eigenverft.NetLib.Infrastructure.Hosting.DirectoryLayout;

var builder = HostApplicationBuilderFactory.CreateWithDefaultDirectory();
var directories = builder.GetDirectoryLayout();

string settingsDirectory =
    directories[DefaultDirectory.ApplicationSettings];

Console.WriteLine(settingsDirectory);

using IHost host = builder.Build();
await host.RunAsync();
```

The standard layout is created directly below the executable directory:

```text
<application>/
├─ AppLogs/
├─ AppData/
├─ AppState/
├─ AppCerts/
└─ AppSettings/
```

Each directory is created during registration and checked for write access, so path or permission problems fail early during startup instead of surfacing later during normal application work.

The same layout is registered as `IAppDirectoryLayout` when the host is built, so normal constructor injection works as expected:

```csharp
public sealed class Worker(
    ILogger<Worker> logger,
    IAppDirectoryLayout directories) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Using application data directory {Directory}",
            directories[DefaultDirectory.ApplicationData]);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
```

## 🗂️ Override standard folder names

Unspecified standard directories keep their defaults:

```csharp
builder.AddDefaultDirectoryLayout(
    new Dictionary<DefaultDirectory, string>
    {
        [DefaultDirectory.ApplicationData] = "Data",
        [DefaultDirectory.ApplicationLogFiles] = "Logs",
    });
```

## 🧩 Custom directory layouts

Use semantic keys when the standard set is not what your application needs:

```csharp
builder.AddDirectoryLayout(
    new Dictionary<string, string>
    {
        ["Cache"] = "cache",
        ["Imports"] = "incoming",
    });

IAppDirectoryLayout directories = builder.GetDirectoryLayout();
string imports = directories["Imports"];
```

Folder mappings are intentionally direct children of the application root. Rooted paths, nested paths, and traversal patterns are rejected.

## 🔧 Without Generic Host

The same layout primitive can be used directly when no host builder is involved:

```csharp
AppDirectoryLayout directories = AppDirectoryLayoutFactory.CreateDefault();
```

An explicit root can also be supplied to the factory for tools, tests, or custom bootstrap scenarios.

## Certificates

`Eigenverft.NetLib.Infrastructure.Security.Certificates` provides host-independent X.509 helpers:

- `SelfSignedCertificateFactory.Create(...)` creates caller-owned self-signed certificates for TLS server/client, code-signing, and email-protection purposes using RSA or ECDSA profiles.
- `ManagedCertificateFile.LoadOrCreate(...)` loads a managed PFX or returns a policy-controlled recovery certificate. `CertificateRecoveryMode.PreserveExisting` is the safe default and does not overwrite an existing unusable PFX.

The certificate APIs depend only on .NET cryptography and file-system primitives; ASP.NET Core, Kestrel, SNI, configuration, and logging remain outside this layer.

## 🎯 Target frameworks

The package ships dedicated assets for:

- `net8.0`
- `net10.0`

A .NET 9 consumer can use the compatible `net8.0` asset.

## 📚 Documentation

- [GitHub Pages documentation](https://eigenverft.github.io/Eigenverft.NetLib.Infrastructure/)
- [Generated API reference](https://eigenverft.github.io/Eigenverft.NetLib.Infrastructure/api/)

## 🧪 Build and test

From the repository root:

```shell
dotnet build src/Eigenverft.NetLib.Infrastructure.slnx --configuration Release
dotnet test src/Eigenverft.NetLib.Infrastructure.slnx --configuration Release
```

## 🚢 Releases

`main` is the production channel. Every accepted change is built, tested, documented, packed, and published by the repository CI/CD workflow.

Package versions follow the Eigenverft Drydock timestamp-based versioning scheme. Published versions and download history are available on [NuGet.org](https://www.nuget.org/packages/Eigenverft.NetLib.Infrastructure).

## 🤝 Contributing and support

- 🐛 [Open an issue](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/issues)
- 🔧 [Submit a pull request](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/pulls)
- 📦 [View the package on NuGet.org](https://www.nuget.org/packages/Eigenverft.NetLib.Infrastructure)

## 📄 License

Licensed under the [MIT License](LICENSE) by Eigenverft.

---

<div align="center">
Made with ❤️ by Eigenverft
</div>
