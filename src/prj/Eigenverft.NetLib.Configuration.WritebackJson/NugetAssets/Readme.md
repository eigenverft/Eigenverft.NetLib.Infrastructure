# Eigenverft.NetLib.Configuration.WritebackJson

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.NetLib.Configuration.WritebackJson?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.WritebackJson) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.NetLib.Configuration.WritebackJson?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.WritebackJson) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.NetLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.WritebackJson) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)

Manage a typed JSON document with a persisted branch, an initial rollback snapshot, and a separate runtime-only working copy.

## ✨ At a glance

| Capability | What it does | Starting point |
| --- | --- | --- |
| Persisted state | Mutate the current document and save it to JSON | `MutateCurrentAndSave(...)` |
| Runtime state | Keep a detached working copy for unpersisted changes | `MutateRuntimeWorkingCopy(...)` |
| Startup baseline | Retain the original document for explicit rollback | `InitialSnapshot` and restore methods |
| External edits | Optionally watch and reload the backing file | `watchForExternalChanges` |
| Dependency injection | Register a typed store as a singleton | `AddWritebackJsonStore<T>(...)` |

## 📦 Installation

```shell
dotnet add package Eigenverft.NetLib.Configuration.WritebackJson
```

## 🚀 Quick start

The model must be a reference type with a public parameterless constructor:

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

The runtime copy remains `Preview`; changing persisted `Current` does not overwrite it. Call `RestoreRuntimeWorkingCopyFromCurrent()` when the runtime branch should resynchronize with the file-backed state.

## 🧭 State and notifications

At construction, the store loads the JSON document or creates one from `new T()` if the file is absent. It writes the initial document and captures it as `InitialSnapshot`. The snapshot is a deep copy and remains the rollback baseline for the lifetime of that store.

- `Current` is the file-backed branch. Use `MutateCurrentAndSave(...)` to persist changes.
- `RuntimeWorkingCopy` is detached in-memory state. Use `MutateRuntimeWorkingCopy(...)`; this never writes the backing file.
- `CurrentChanged` and `RuntimeWorkingCopyChanged` report changes from store operations. Pass `notify: false` to suppress an operation's notification.
- `GetCurrentSnapshot()` returns an independent copy of the current state.

Treat the objects returned by `Current` and `RuntimeWorkingCopy` as read-only. Directly changing their properties bypasses persistence and change notifications.

## 👀 External changes and DI

External-file watching is enabled by default. Successfully reloaded content updates `Current`; it does not replace `InitialSnapshot` or `RuntimeWorkingCopy`. Handle `ErrorOccurred` for background file-system or reload errors. Dispose the store when its lifetime ends.

To register it in a host's service collection:

```csharp
using Eigenverft.NetLib.Configuration.WritebackJson;
builder.Services.AddWritebackJsonStore<RuntimeSettings>(
    "AppSettings/runtime-settings.json",
    watchForExternalChanges: true);
```

The store is not an `IConfiguration` provider and does not publish configuration reload tokens. It can share a file with a JSON configuration provider or [SwitchableJson](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.SwitchableJson); configure that provider's reload behavior separately if it should observe writes.

## 🎯 Target frameworks

- `net8.0`
- `net10.0`

A .NET 9 consumer can use the compatible `net8.0` asset.

## 🔗 Project links

- [Source project](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/tree/main/src/prj/Eigenverft.NetLib.Configuration.WritebackJson)
- [Solution documentation](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/tree/main/src/sln/Eigenverft.NetLib.Configuration.WritebackJson)
- [Repository](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure)

## 📄 License

[MIT](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)
