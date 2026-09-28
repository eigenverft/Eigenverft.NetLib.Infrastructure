# Eigenverft.NetLib.Logging.Deferred

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.NetLib.Logging.Deferred?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Logging.Deferred) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.NetLib.Logging.Deferred?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Logging.Deferred) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.NetLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.NetLib.Logging.Deferred) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)

Defer message and structured-argument work until the underlying Microsoft logger enables the requested level. Use standard ILogger categories and providers with lazy factories for expensive diagnostics.

## ✨ At a glance

| | |
| --- | --- |
| Package | Eigenverft.NetLib.Logging.Deferred |
| APIs | IDeferredLogger<TCategoryName>, AddDeferredLogging(), ToDeferred() |
| Integration | Microsoft.Extensions.Logging and dependency injection |
| Target frameworks | .NET 8 and .NET 10 |
| License | MIT |

## 📦 Installation

```shell
dotnet add package Eigenverft.NetLib.Logging.Deferred
```

## 🚀 Quick start

Register the adapters with the Generic Host. A factory argument is evaluated only when the underlying logger enables that level:

```csharp
using System.IO;
using Eigenverft.NetLib.Logging.Deferred;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddDeferredLogging();

using IHost host = builder.Build();
IDeferredLogger<Program> logger =
    host.Services.GetRequiredService<IDeferredLogger<Program>>();

string settingsDirectory = Path.Combine(
    builder.Environment.ContentRootPath,
    "settings");
logger.LogDebug(
    "Discovered {FileCount} files",
    () => Directory.GetFiles(settingsDirectory).Length);
logger.LogInformation(
    () => $"Loaded settings from {settingsDirectory}.");
```

The same interfaces support standard levels and exception overloads. Structured message templates remain intact; only the supplied message or argument factories are evaluated lazily.

## 🧭 Registration and evaluation behavior

AddDeferredLogging() registers singleton generic and non-generic adapters over the normal ILogger<T>, ILogger, and ILoggerFactory services. It does not configure a logging provider. Generic Host applications already provide those services; with a plain service collection, register logging with services.AddLogging() first.

Factory overloads such as LogDebug("Count {Count}", () => ComputeCount()) avoid the computation when Debug is disabled. Eager values such as LogDebug("Count {Count}", ComputeCount()) are evaluated by the caller before the method is invoked. A disabled level skips factory evaluation; an enabled level evaluates each supplied factory once.

logger.ToDeferred() adapts an existing typed or untyped ILogger, including a bootstrap logger. The adapter uses that logger directly and preserves its category. This is lazy evaluation, not buffering: the wrapped logger is supplied when the adapter is created, and no messages are queued while waiting for a provider.

## 🎯 Target frameworks

The package targets net8.0 and net10.0.

## 🔗 Project links

- [NuGet package](https://www.nuget.org/packages/Eigenverft.NetLib.Logging.Deferred)
- [Package source](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/tree/main/src/prj/Eigenverft.NetLib.Logging.Deferred)
- [Solution guide](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/src/sln/Eigenverft.NetLib.Logging.Deferred/Readme.md)

## 📄 License

[MIT License](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)
