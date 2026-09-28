# Eigenverft.NetLib.Configuration.WritebackJson

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.NetLib.Configuration.WritebackJson?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.WritebackJson) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.NetLib.Configuration.WritebackJson?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.WritebackJson) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.NetLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.WritebackJson) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)

Manage a typed JSON document with a file-backed current state, an initial rollback snapshot, and a separate runtime-only working copy.

## At a glance

| Need | API |
| --- | --- |
| Persist a mutation | `MutateCurrentAndSave(...)` |
| Change runtime-only state | `MutateRuntimeWorkingCopy(...)` |
| Resynchronize or restore branches | `Restore...` methods |

## Quick start

```csharp
using Eigenverft.NetLib.Configuration.WritebackJson;

using var store = new WritebackJsonStore<RuntimeSettings>(
    "AppSettings/runtime-settings.json");

store.MutateRuntimeWorkingCopy(settings => settings.Route = "Preview");
store.MutateCurrentAndSave(settings => settings.Route = "Failover");

public sealed class RuntimeSettings
{
    public string Route { get; set; } = "Primary";
}
```

The runtime branch remains `Preview`; persisting `Current` as `Failover` does not replace it. This store is not an `IConfiguration` provider. See the [NuGet README](../../prj/Eigenverft.NetLib.Configuration.WritebackJson/NugetAssets/Readme.md) for notifications, external-file watching, dependency-injection registration, and state semantics.

## Development

Run `dotnet test` from this directory to test the package for all target frameworks.
