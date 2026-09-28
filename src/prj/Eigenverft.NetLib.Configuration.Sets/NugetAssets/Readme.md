# Eigenverft.NetLib.Configuration.Sets

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.NetLib.Configuration.Sets?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Sets) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.NetLib.Configuration.Sets?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Sets) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.NetLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Sets) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)

Coordinate related JSON configuration files as one application-defined profile choice. Each participant is prepared before the set changes, so a rejected candidate leaves the previous coordinated profile active.

## ✨ At a glance

| | |
| --- | --- |
| Package | `Eigenverft.NetLib.Configuration.Sets` |
| Main APIs | `AddConfigurationSet(...)`, `IConfigurationSetManager` |
| Source integration | `Eigenverft.NetLib.Configuration.SwitchableJson` |
| Profile meanings | Defined by the application |
| Target frameworks | .NET 8 and .NET 10 |
| License | MIT |

## 📦 Installation

```shell
dotnet add package Eigenverft.NetLib.Configuration.Sets
```

The package depends on [Eigenverft.NetLib.Configuration.SwitchableJson](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.SwitchableJson); NuGet restores it automatically.

## 🚀 Quick start

Register a named set and bind one or more JSON files using the default `{rootPath}/{value}/{fileName}` layout. Create a directory and valid file for every value that should be selectable:

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

The manager is registered automatically with the first set. The synchronous call returns only after all participants have completed; inspect `result` when it returns false.

## 🧭 Set behavior and related APIs

Set names, allowed values, paths, and policy meanings belong to the application. Runtime switches update ordinary `IConfiguration` values, so consumers must be reload-aware (for example, `IOptionsMonitor<T>`) to react to changes. Startup-only concerns such as the DI graph or middleware composition should wait for restart; use `.ApplyMode(ConfigurationSetApplyMode.StartupOnly)` when storing desired state. Sets coordinate [Configuration.SwitchableJson](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.SwitchableJson) sources; they do not define what a profile means. Value protection is an opt-in feature of those sources, using [Configuration.Values](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Values) codecs.

For custom file naming, register each participant with a `sourcePathResolver`. The `ConfigurationSetRegistration` returned by `AddConfigurationSet` is a startup handle; runtime switching normally goes through `IConfigurationSetManager`. The keyed `IConfigurationSetCoordinator` and `IConfigurationSetEventHub` provide set-level integration and observation.

`AddConfigurationSetStateFile(path)` adds an optional self-describing desired-state file, watcher, and DI state-store service for persisted operator or control-plane choices. Keep that state file separate from the profile files. For a single independently switched file without set coordination, use `Eigenverft.NetLib.Configuration.SwitchableJson` directly.

## 🎯 Target frameworks

The package targets `net8.0` and `net10.0`.

## 🔗 Project links

- [NuGet package](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Sets)
- [Package source](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/tree/main/src/prj/Eigenverft.NetLib.Configuration.Sets)
- [Solution guide](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/src/sln/Eigenverft.NetLib.Configuration.Sets/Readme.md)

## 📄 License

[MIT License](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)
