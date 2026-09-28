# Eigenverft.NetLib.Configuration.Sources

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.NetLib.Configuration.Sources?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Sources) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.NetLib.Configuration.Sources?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Sources) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.NetLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Sources) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)

Replace the Generic Host's default configuration providers with a minimal set of environment variables and optional process command-line arguments.

| Package | Primary API | Target frameworks |
| --- | --- | --- |
| `Eigenverft.NetLib.Configuration.Sources` | `ResetToMinimalConfigurationSources(...)` | .NET 8, .NET 10 |

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

The reset clears all existing providers, so call it before adding application sources you want to retain. Command-line arguments are optional and disabled by default; when enabled, they take precedence over environment variables, while later custom providers can take precedence over both. To inspect the final precedence after this reset and subsequent registrations, use [Configuration.Diagnostics](../Eigenverft.NetLib.Configuration.Diagnostics/Readme.md). See the [NuGet package guide](../../prj/Eigenverft.NetLib.Configuration.Sources/NugetAssets/Readme.md) for argument details.

## Development

Run package tests for both target frameworks from this directory:

```bash
dotnet test
```
