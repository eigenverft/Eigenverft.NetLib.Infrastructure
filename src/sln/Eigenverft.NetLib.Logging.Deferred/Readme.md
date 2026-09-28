# Eigenverft.NetLib.Logging.Deferred

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.NetLib.Logging.Deferred?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Logging.Deferred) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.NetLib.Logging.Deferred?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Logging.Deferred) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.NetLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.NetLib.Logging.Deferred) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)

Lazy adapters for Microsoft logging that skip message and argument-factory work when the underlying log level is disabled.

| Package | Primary APIs | Target frameworks |
| --- | --- | --- |
| Eigenverft.NetLib.Logging.Deferred | IDeferredLogger<TCategoryName>, AddDeferredLogging(), ToDeferred() | .NET 8, .NET 10 |

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

The factories run only when the underlying logger enables the level, so the file scan and interpolated message are deferred—not buffered. `ToDeferred()` wraps an existing logger immediately; [Logging.Bootstrap](../Eigenverft.NetLib.Logging.Bootstrap/Readme.md) documents startup logger setup. See the [NuGet package guide](../../prj/Eigenverft.NetLib.Logging.Deferred/NugetAssets/Readme.md) for registration and evaluation limits.

## Development

Run package tests for both target frameworks from this directory:

```bash
dotnet test
```
