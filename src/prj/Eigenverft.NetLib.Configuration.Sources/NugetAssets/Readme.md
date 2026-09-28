# Eigenverft.NetLib.Configuration.Sources

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.NetLib.Configuration.Sources?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Sources) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.NetLib.Configuration.Sources?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Sources) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.NetLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Sources) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)

Reset a host builder's configuration providers to environment variables and, optionally, the current process command line. Add application-specific sources afterward.

## ✨ At a glance

| | |
| --- | --- |
| Package | `Eigenverft.NetLib.Configuration.Sources` |
| Main API | `ResetToMinimalConfigurationSources(...)` |
| Builder | `IHostApplicationBuilder` |
| Defaults | Environment variables on; command-line arguments off |
| Target frameworks | .NET 8 and .NET 10 |
| License | MIT |

## 📦 Installation

```shell
dotnet add package Eigenverft.NetLib.Configuration.Sources
```

## 🚀 Quick start

Call the reset before adding custom configuration providers:

```csharp
using Eigenverft.NetLib.Configuration.Sources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.ResetToMinimalConfigurationSources(
    includeCommandLineArguments: true);

builder.Configuration.AddJsonFile(
    "appsettings.json",
    optional: true,
    reloadOnChange: true);

using IHost host = builder.Build();
await host.RunAsync();
```

The method returns the same builder for chaining. Its defaults retain environment variables and omit command-line arguments; the example opts into both. Since the reset clears the existing source collection, call it before adding sources you want to keep.

## 🧭 Source order and behavior

The method removes all existing configuration sources, including the host builder's defaults. It then adds environment variables first and, when requested, command-line arguments second, so command-line values have higher precedence than environment variables. Any custom provider added after the reset is later in the stack and can take higher precedence.

When command-line arguments are enabled, the helper reads the current process arguments with `Environment.GetCommandLineArgs()` and skips the executable path. It does not take an argument array parameter.

After registering the sources you want to retain, use [Configuration.Diagnostics](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/src/sln/Eigenverft.NetLib.Configuration.Diagnostics/Readme.md) to inspect their final precedence.

## 🎯 Target frameworks

The package targets `net8.0` and `net10.0`.

## 🔗 Project links

- [NuGet package](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Sources)
- [Package source](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/tree/main/src/prj/Eigenverft.NetLib.Configuration.Sources)
- [Solution guide](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/src/sln/Eigenverft.NetLib.Configuration.Sources/Readme.md)

## 📄 License

[MIT License](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)
