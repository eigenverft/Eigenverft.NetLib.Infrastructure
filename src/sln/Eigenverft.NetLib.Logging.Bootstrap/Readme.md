# Eigenverft.NetLib.Logging.Bootstrap

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.NetLib.Logging.Bootstrap?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Logging.Bootstrap) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.NetLib.Logging.Bootstrap?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Logging.Bootstrap) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.NetLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.NetLib.Logging.Bootstrap) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)

Create startup loggers before the host is built, using the initialized Serilog logger when available and a Microsoft logging fallback otherwise.

## At a glance

| Need | API |
| --- | --- |
| Create a typed startup logger | `BootstrapLogger<TCategoryName>.CreateLogger(...)` |
| Create a named startup logger | `BootstrapLogger.CreateLogger(...)` |
| Require an isolated Serilog logger | `CreateRequiredSerilogLogger(...)` |

## Quick start

```csharp
using Eigenverft.NetLib.Logging.Bootstrap;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
ILogger startupLogger = BootstrapLogger.CreateLogger("Startup", builder.Configuration);
startupLogger.LogInformation(
    "Starting in {Environment}",
    builder.Environment.EnvironmentName);

using IHost host = builder.Build();
await host.RunAsync();
```

The first successful factory creation is cached for the process. After registering configuration sources and before `Build()`, the optional [Eigenverft.NetLib.Configuration.Diagnostics](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Diagnostics) package can report provider precedence and shadowed keys through this startup logger; it never logs configuration values and is not a Bootstrap dependency. See the [NuGet README](../../prj/Eigenverft.NetLib.Logging.Bootstrap/NugetAssets/Readme.md) for backend selection and strict Serilog setup.

## Development

Run `dotnet test` from this directory to test the package for all target frameworks.
