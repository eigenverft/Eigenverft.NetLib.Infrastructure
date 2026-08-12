# 🧱 Eigenverft.NetLib.Infrastructure

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.NetLib.Infrastructure?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Infrastructure) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.NetLib.Infrastructure?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Infrastructure) [![Targets](https://img.shields.io/badge/targets-.NET%208%20%7C%2010-512BD4?logo=dotnet&logoColor=white)](#-target-frameworks) [![License](https://img.shields.io/github/license/eigenverft/Eigenverft.NetLib.Infrastructure?logo=mit)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)

Small, reusable infrastructure primitives for .NET applications and Generic Host-based services.

Provides predictable, executable-rooted application directories with automatic creation and writable validation.

Also includes generic reversible string transforms, JSON-safe Base92 representation, machine binding, DPAPI machine-scope transforms, certificate primitives, and pre-host bootstrap logging.

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

Each directory is created during registration and checked for write access, so path or permission problems fail early during startup.

The same layout is registered as `IAppDirectoryLayout` after `Build()`, so normal constructor injection works as expected:

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

```csharp
builder.AddDefaultDirectoryLayout(
    new Dictionary<DefaultDirectory, string>
    {
        [DefaultDirectory.ApplicationData] = "Data",
        [DefaultDirectory.ApplicationLogFiles] = "Logs",
    });
```

Unspecified standard directories retain their defaults.

## 🧩 Custom directory layouts

```csharp
builder.AddDirectoryLayout(
    new Dictionary<string, string>
    {
        ["Cache"] = "cache",
        ["Imports"] = "incoming",
    });

string imports = builder.GetDirectoryLayout()["Imports"];
```

Folder mappings are intentionally direct children of the application root. Rooted paths, nested paths, and traversal patterns are rejected.

## 🔧 Without Generic Host

```csharp
AppDirectoryLayout directories = AppDirectoryLayoutFactory.CreateDefault();
```

An explicit root can also be supplied to the factory for tools, tests, or custom bootstrap scenarios.

## Certificates

`Eigenverft.NetLib.Infrastructure.Security.Certificates` provides host-independent X.509 helpers:

- `SelfSignedCertificateFactory.Create(...)` creates caller-owned self-signed certificates for TLS server/client, code-signing, and email-protection purposes using RSA or ECDSA profiles.
- `ManagedCertificateFile.LoadOrCreate(...)` loads a managed PFX or returns a policy-controlled recovery certificate. `CertificateRecoveryMode.PreserveExisting` is the safe default and does not overwrite an existing unusable PFX.

The certificate APIs have no ASP.NET Core, Kestrel, SNI, configuration, or logging dependency.

## 🎯 Target frameworks

The package ships dedicated assets for:

- `net8.0`
- `net10.0`

A .NET 9 consumer can use the compatible `net8.0` asset.

## 🔗 Project links

- [GitHub repository](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure)
- [Documentation](https://eigenverft.github.io/Eigenverft.NetLib.Infrastructure/)
- [Issues](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/issues)
- [NuGet package](https://www.nuget.org/packages/Eigenverft.NetLib.Infrastructure)

## 📄 License

Licensed under the [MIT License](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE) by Eigenverft.

---

Made with ❤️ by Eigenverft
