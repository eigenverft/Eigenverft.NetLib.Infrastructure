# Eigenverft.NetLib.Configuration.Diagnostics

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.NetLib.Configuration.Diagnostics?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Diagnostics) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.NetLib.Configuration.Diagnostics?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Diagnostics) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.NetLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Diagnostics) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)

Log configuration-provider precedence and shadowed keys during Generic Host startup. The diagnostics report provider origins and key paths, never values.

| Package | Primary API | Target frameworks |
| --- | --- | --- |
| `Eigenverft.NetLib.Configuration.Diagnostics` | `builder.LogConfigurationResolution(logger)` | .NET 8, .NET 10 |

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

Call after all providers have been registered and before `Build()`. Provider order is reported highest precedence first; opaque providers may make the collision scan incomplete, and values are never logged. This package consumes an `ILogger` but does not create one: [Logging.Bootstrap](../Eigenverft.NetLib.Logging.Bootstrap/Readme.md) can provide startup logging before the host exists. If you intentionally reset host defaults first, see [Configuration.Sources](../Eigenverft.NetLib.Configuration.Sources/Readme.md), then add the providers to inspect. The [NuGet package guide](../../prj/Eigenverft.NetLib.Configuration.Diagnostics/NugetAssets/Readme.md) covers lower-level diagnostic calls.

## Development

Run package tests for both target frameworks from this directory:

```bash
dotnet test
```
