# Eigenverft.NetLib.Configuration.Diagnostics

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.NetLib.Configuration.Diagnostics?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Diagnostics) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.NetLib.Configuration.Diagnostics?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Diagnostics) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.NetLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Diagnostics) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)

Log which provider wins and where configuration keys are shadowed during host startup. Diagnostics report provider origins and key paths without logging configuration values.

## ✨ At a glance

| | |
| --- | --- |
| Package | `Eigenverft.NetLib.Configuration.Diagnostics` |
| Main API | `builder.LogConfigurationResolution(logger)` |
| Scope | `IHostApplicationBuilder` before `Build()` |
| Reports | Provider order and keys present in multiple providers |
| Target frameworks | .NET 8 and .NET 10 |
| License | MIT |

## 📦 Installation

```shell
dotnet add package Eigenverft.NetLib.Configuration.Diagnostics
```

## 🚀 Quick start

Add the configuration providers first, then log the completed precedence and collision scan before building the host:

```csharp
using Eigenverft.NetLib.Configuration.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddJsonFile(
    "other-settings.json",
    optional: true,
    reloadOnChange: true);

using ILoggerFactory loggerFactory =
    LoggerFactory.Create(logging => logging.AddConsole());
ILogger startupLogger = loggerFactory.CreateLogger("ConfigurationStartup");

builder.LogConfigurationResolution(startupLogger);

using IHost host = builder.Build();
await host.RunAsync();
```

The helper returns the same builder for chaining. Lower-level calls, `LogProviderOrder(configuration, logger)` and `LogKeyCollisions(configuration, logger)`, are available when only one diagnostic is needed.

## 🔎 What is reported

Provider order is written highest precedence first; the last registered provider wins in the standard .NET configuration stack. Collision output lists each complete key path and its winner-to-shadowed provider chain. JSON origins use the file name rather than the full path.

Configuration values are never logged. Collision inspection reads provider data for diagnostics; a provider whose data cannot be inspected is reported as an incomplete scan, so collision results may be partial for opaque custom providers.

Call after all configuration sources have been registered and before `Build()`. This package consumes but does not create an `ILogger`; [Logging.Bootstrap](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/src/sln/Eigenverft.NetLib.Logging.Bootstrap/Readme.md) can provide startup logging before the host exists. If you intentionally reset host defaults first, see [Configuration.Sources](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/src/sln/Eigenverft.NetLib.Configuration.Sources/Readme.md), then add the providers to inspect.

## 🎯 Target frameworks

The package targets `net8.0` and `net10.0`.

## 🔗 Project links

- [NuGet package](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Diagnostics)
- [Package source](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/tree/main/src/prj/Eigenverft.NetLib.Configuration.Diagnostics)
- [Solution guide](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/src/sln/Eigenverft.NetLib.Configuration.Diagnostics/Readme.md)

## 📄 License

[MIT License](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)
