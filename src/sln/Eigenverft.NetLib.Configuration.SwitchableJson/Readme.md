# Eigenverft.NetLib.Configuration.SwitchableJson

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.NetLib.Configuration.SwitchableJson?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.SwitchableJson) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.NetLib.Configuration.SwitchableJson?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.SwitchableJson) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.NetLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.SwitchableJson) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)

Register JSON settings with candidate preparation, last-known-good failure behavior, optional file watching, and runtime source switching.

| Package | Primary APIs | Related packages | Target frameworks |
| --- | --- | --- | --- |
| `Eigenverft.NetLib.Configuration.SwitchableJson` | `AddSwitchableJsonFile(...)`, `ISwitchableJsonConfiguration` | `Configuration.Sets`, `Configuration.Values` | .NET 8, .NET 10 |

```csharp
using System.IO;
using Eigenverft.NetLib.Configuration.SwitchableJson;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
string settings = Path.Combine(builder.Environment.ContentRootPath, "settings");
string normalPath = Path.Combine(settings, "Operational.Normal.json");
string incidentPath = Path.Combine(settings, "Operational.Incident.json");

builder.AddSwitchableJsonFile(
    name: "OperationalSettings",
    initialPath: normalPath,
    reloadOnChange: true);

using IHost host = builder.Build();
ISwitchableJsonConfiguration source =
    host.Services.GetRequiredKeyedService<ISwitchableJsonConfiguration>(
        "OperationalSettings");

SwitchableJsonSwitchResult result = source.TrySwitch(incidentPath);
if (!result.Succeeded)
{
    // The previous active source and published configuration remain in place.
}
```

Create both files with valid JSON before running this example; the initial file is required by default. A failed candidate keeps the last successful snapshot. Use [Configuration Sets](../Eigenverft.NetLib.Configuration.Sets/Readme.md) to coordinate several files, or opt into codecs from [Configuration.Values](../Eigenverft.NetLib.Configuration.Values/Readme.md) through `ValueProtection`. The [NuGet package guide](../../prj/Eigenverft.NetLib.Configuration.SwitchableJson/NugetAssets/Readme.md) covers preparation, reload, and protection caveats.

## Development

Run package tests for both target frameworks from this directory:

```bash
dotnet test
```
