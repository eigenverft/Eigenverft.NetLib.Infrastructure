# Eigenverft.NetLib.Configuration.Sets

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.NetLib.Configuration.Sets?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Sets) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.NetLib.Configuration.Sets?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Sets) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.NetLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Sets) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)

Coordinate related JSON configuration sources behind one application-defined profile value. All registered participants must prepare successfully before a set switch commits.

| Package | Primary APIs | Dependency | Target frameworks |
| --- | --- | --- | --- |
| `Eigenverft.NetLib.Configuration.Sets` | `AddConfigurationSet(...)`, `IConfigurationSetManager` | `Configuration.SwitchableJson` | .NET 8, .NET 10 |

```csharp
using System.IO;
using Eigenverft.NetLib.Configuration.Sets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
string profilesRoot = Path.Combine(
    builder.Environment.ContentRootPath,
    "settings",
    "Operations");

builder.AddConfigurationSet(
        name: "OperationalProfile",
        initialValue: "Normal",
        additionalAllowedValues: ["Incident"])
    .AddSwitchableJson(
        rootPath: profilesRoot,
        fileNames: ["LoggerSettings.json", "Resilience.json"]);

using IHost host = builder.Build();
IConfigurationSetManager profiles =
    host.Services.GetRequiredService<IConfigurationSetManager>();

bool switched = profiles.TrySwitchRuntime(
    setName: "OperationalProfile",
    value: "Incident",
    result: out ConfigurationSetSwitchResult? result);
```

The default paths are `<rootPath>/<value>/<fileName>`; create valid files for each selectable value. The synchronous switch succeeds only after all registered sources prepare successfully; inspect `result` on failure. Sets coordinate [Configuration.SwitchableJson](../Eigenverft.NetLib.Configuration.SwitchableJson/Readme.md) sources but do not assign meaning to profile values. For opt-in value protection on those sources, see [Configuration.Values](../Eigenverft.NetLib.Configuration.Values/Readme.md). The [NuGet package guide](../../prj/Eigenverft.NetLib.Configuration.Sets/NugetAssets/Readme.md) describes persistence and startup-only settings.

## Development

Run package tests for both target frameworks from this directory:

```bash
dotnet test
```
