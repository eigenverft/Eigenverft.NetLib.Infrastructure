# Eigenverft.NetLib.Configuration.SwitchableJson

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.NetLib.Configuration.SwitchableJson?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.SwitchableJson) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.NetLib.Configuration.SwitchableJson?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.SwitchableJson) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.NetLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.SwitchableJson) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)

Register JSON as a normal configuration provider, with an isolated preparation step before publication and a stable runtime handle for deliberate source changes.

## ✨ At a glance

| | |
| --- | --- |
| Package | `Eigenverft.NetLib.Configuration.SwitchableJson` |
| Registration | `AddSwitchableJsonFile(...)` |
| Runtime API | `ISwitchableJsonConfiguration.TrySwitch(...)` |
| Failure behavior | Reject failed candidates; keep the active snapshot by default |
| Optional capabilities | File watching, candidate preparation, value protection |
| Target frameworks | .NET 8 and .NET 10 |
| License | MIT |

## 📦 Installation

```shell
dotnet add package Eigenverft.NetLib.Configuration.SwitchableJson
```

## 🚀 Quick start

Create both files with valid JSON before running this example; the initial file is required by default. Resolve a keyed runtime handle from DI after building the host to switch to another file:

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

The provider stays at its registered precedence. Changes flow through normal `IConfiguration` reload behavior when the effective values change.

## 🧭 Preparation, reload, and protection

An initial path may be absolute or relative to the host content root. Each initial load, file reload, or requested source switch is parsed and prepared as a candidate before the published snapshot changes. A missing, invalid, inaccessible, or rejected candidate leaves the last successful snapshot active. `ReloadOnChange` is opt-in; the default is false. Manual failures keep the current source by default, or can be configured to throw with `SwitchableJsonRuntimeFailurePolicy.Throw`.

Set `SwitchableJsonRegistrationOptions.CandidatePreparation` to an application-defined `IJsonConfigurationSourcePreparation` or a bundle from `JsonConfigurationCandidatePreparations.Compose(...)`. Preparation steps run in order against an isolated candidate. If several sources must change as one unit, use `Eigenverft.NetLib.Configuration.Sets`, which builds on this package.

Optional `ValueProtection` selects JSON key names or full configuration paths and uses codecs from `Eigenverft.NetLib.Configuration.Values`. Matching plaintext values may be written back in encoded form during registration or later source loads, so the application needs write access when matching clear text is present. Protection is load-bound; it is not a continuous file-watcher invariant. The registered decoder runs before candidate preparation. Codec strength varies: reversible transforms such as Base64 or ROT13 are not cryptographic confidentiality; choose an appropriate codec for the deployment threat model.

The runtime handle exposes `LifecycleChanged` independently of `IConfiguration` change tokens. A completed switch or reload can be observable through that event even when the effective key/value snapshot is unchanged. Do not use ordinary file reload as a transaction mechanism across multiple files; use Configuration Sets for that.

## 🎯 Target frameworks

The package targets `net8.0` and `net10.0`.

## 🔗 Project links

- [NuGet package](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.SwitchableJson)
- [Package source](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/tree/main/src/prj/Eigenverft.NetLib.Configuration.SwitchableJson)
- [Solution guide](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/src/sln/Eigenverft.NetLib.Configuration.SwitchableJson/Readme.md)
- [Configuration.Values package](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Values)

## 📄 License

[MIT License](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)
