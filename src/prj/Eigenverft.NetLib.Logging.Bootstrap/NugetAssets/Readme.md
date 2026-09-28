# Eigenverft.NetLib.Logging.Bootstrap

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.NetLib.Logging.Bootstrap?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Logging.Bootstrap) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.NetLib.Logging.Bootstrap?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Logging.Bootstrap) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.NetLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.NetLib.Logging.Bootstrap) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)

Log startup diagnostics before the host is built, using an initialized Serilog logger when available and otherwise a Microsoft logging fallback.

## ✨ At a glance

| Capability | What it does | Starting point |
| --- | --- | --- |
| Typed logger | Create a logger categorized by a type | `BootstrapLogger<TCategoryName>.CreateLogger(...)` |
| Named logger | Create a logger with an explicit category name | `BootstrapLogger.CreateLogger(...)` |
| Automatic backend | Capture an existing Serilog logger or create a Microsoft logging fallback | `CreateLogger(...)` |
| Required Serilog | Create a Serilog logger from an isolated JSON file and fail if unavailable | `CreateRequiredSerilogLogger(...)` |

## 📦 Installation

```shell
dotnet add package Eigenverft.NetLib.Logging.Bootstrap
```

## 🚀 Quick start

Create the bootstrap logger after configuring the builder but before building the host:

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

The first successful factory creation is cached for the process. Later configuration arguments or application logging reconfiguration do not replace the bootstrap channel. The returned factory is process-owned; callers must not dispose it.

## 🪵 Backend behavior

The automatic API captures the current global Serilog logger when Serilog and its Microsoft logging bridge are available and that logger is not Serilog's built-in silent default. Otherwise it uses Microsoft logging, applying the `Logging` section and adding Console support when the corresponding extension assemblies are available. The package has no compile-time Serilog dependency; the consuming application supplies Serilog and its bridge if it wants the automatic Serilog path.

For an explicitly required, isolated Serilog setup, the default file is `AppSettings/BootstrapLoggerSettings.json` beneath `AppContext.BaseDirectory`:

```csharp
ILogger<Program> startupLogger =
    BootstrapLogger<Program>.CreateRequiredSerilogLogger();
```

This strict method does not fall back to Microsoft logging. The consuming application must provide Serilog core, Serilog.Settings.Configuration, Serilog.Extensions.Logging, and every sink or enricher named in the file. It reads only that JSON file and section; it does not load the host's other configuration sources. This method should be the first bootstrap-logger initialization in the process. Optional `reloadOnChange` updates Serilog levels and switches, but does not rebuild its sink pipeline.

`LogConfigurationResolution(...)` belongs to [Eigenverft.NetLib.Configuration.Diagnostics](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Diagnostics). The early environment helper is in [Eigenverft.NetLib.Hosting.DirectoryLayout](https://www.nuget.org/packages/Eigenverft.NetLib.Hosting.DirectoryLayout).

To diagnose provider precedence before host construction, install `Eigenverft.NetLib.Configuration.Diagnostics`, then call the extension after registering configuration sources and pass the bootstrap logger:

```csharp
using Eigenverft.NetLib.Configuration.Diagnostics;
using Eigenverft.NetLib.Logging.Bootstrap;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
ILogger startupLogger = BootstrapLogger.CreateLogger("Startup", builder.Configuration);
builder.LogConfigurationResolution(startupLogger);

using IHost host = builder.Build();
await host.RunAsync();
```

This is an optional package pairing, not a dependency; the diagnostics report provider origins and key-shadow chains, never configuration values.

## 🎯 Target frameworks

- `net8.0`
- `net10.0`

A .NET 9 consumer can use the compatible `net8.0` asset.

## 🔗 Project links

- [Source project](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/tree/main/src/prj/Eigenverft.NetLib.Logging.Bootstrap)
- [Solution documentation](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/tree/main/src/sln/Eigenverft.NetLib.Logging.Bootstrap)
- [Repository](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure)

## 📄 License

[MIT](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)
