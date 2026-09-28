# Eigenverft.NetLib.Hosting.DirectoryLayout

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.NetLib.Hosting.DirectoryLayout?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Hosting.DirectoryLayout) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.NetLib.Hosting.DirectoryLayout?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Hosting.DirectoryLayout) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.NetLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.NetLib.Hosting.DirectoryLayout) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)

Create predictable writable application folders below the executable directory and resolve the host environment before a Generic Host or ASP.NET Core builder exists.

## ✨ At a glance

| Capability | What it does | Starting point |
| --- | --- | --- |
| Standard folders | Create the conventional logs, data, state, keys, certificates, and settings folders | `AddDefaultDirectoryLayout()` |
| Custom folders | Map application-defined semantic keys to direct child folders | `AddDirectoryLayout(...)` |
| Host integration | Register and retrieve the layout through `IHostApplicationBuilder` and DI | `IAppDirectoryLayout` |
| Early environment | Resolve the process host environment before builder creation | `StaticHostEnvironment` |

## 📦 Installation

```shell
dotnet add package Eigenverft.NetLib.Hosting.DirectoryLayout
```

## 🚀 Quick start

```csharp
using System;
using Eigenverft.NetLib.Hosting.DirectoryLayout;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.AddDefaultDirectoryLayout();

IAppDirectoryLayout directories = builder.GetDirectoryLayout();
string settings = directories[DefaultDirectory.ApplicationSettings];
Console.WriteLine(settings);

using IHost host = builder.Build();
await host.RunAsync();
```

By default, the folders are direct children of `AppContext.BaseDirectory`. Registration creates each directory and probes it for write access so filesystem or permission errors surface during startup.

For ASP.NET Core, [Eigenverft.WebLib.Hosting.DirectoryLayout](https://www.nuget.org/packages/Eigenverft.WebLib.Hosting.DirectoryLayout) builds on this package: it roots `ContentRootPath` at `AppContext.BaseDirectory`, sets `WebRootPath` to `wwwroot`, and exposes that folder through the semantic `Web` mapping. It delegates the shared application-directory creation and validation to this package; the NetLib package itself remains independent of ASP.NET Core.

## 📁 Standard and custom folders

The standard names are `AppLogs`, `AppData`, `AppState`, `AppProtectionKeys`, `AppCerts`, and `AppSettings`. Override only the names that need changing:

```csharp
using System.Collections.Generic;
builder.AddDefaultDirectoryLayout(
    new Dictionary<DefaultDirectory, string>
    {
        [DefaultDirectory.ApplicationData] = "Data",
        [DefaultDirectory.ApplicationLogFiles] = "Logs",
    });
```

For app-specific keys, use a custom map:

```csharp
using System.Collections.Generic;
builder.AddDirectoryLayout(
    new Dictionary<string, string>
    {
        ["Cache"] = "cache",
        ["Imports"] = "incoming",
    });

string imports = builder.GetDirectoryLayout()["Imports"];
```

Folder names must be single direct-child names. Rooted paths, separators, nested folders, and traversal patterns are rejected. The factory also supports an explicit root and optional writable checks when a Generic Host builder is not involved:

```csharp
AppDirectoryLayout layout = AppDirectoryLayoutFactory.CreateDefault(
    rootPath: "path/to/root",
    verifyWritable: false);
```

## 🌱 Resolve the environment early

`StaticHostEnvironment` is available before creating a host builder, for bootstrap settings or other early startup decisions:

```csharp
using Eigenverft.NetLib.Hosting.DirectoryLayout;
string environment = StaticHostEnvironment.EnvironmentName;
bool useDevelopmentBootstrap = StaticHostEnvironment.IsDevelopment;
bool useQaBootstrap = StaticHostEnvironment.IsEnvironment("QA");
```

Resolution precedence is process command-line arguments, then `DOTNET_ENVIRONMENT`, then `ASPNETCORE_ENVIRONMENT`, then `Production`. The value is captured once when the type is first initialized; arbitrary environment names are preserved and comparisons are case-insensitive.

## 🎯 Target frameworks

- `net8.0`
- `net10.0`

A .NET 9 consumer can use the compatible `net8.0` asset.

## 🔗 Project links

- [Source project](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/tree/main/src/prj/Eigenverft.NetLib.Hosting.DirectoryLayout)
- [Solution documentation](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/tree/main/src/sln/Eigenverft.NetLib.Hosting.DirectoryLayout)
- [Repository](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure)

## 📄 License

[MIT](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)
