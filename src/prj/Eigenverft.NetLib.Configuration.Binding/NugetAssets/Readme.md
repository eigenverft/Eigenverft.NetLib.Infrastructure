# Eigenverft.NetLib.Configuration.Binding

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.NetLib.Configuration.Binding?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Binding) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.NetLib.Configuration.Binding?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Binding) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.NetLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Binding) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)

Make configured lists and dictionaries replace initialized code defaults during standard .NET configuration binding. Choose explicitly what an empty configured collection means.

## ✨ At a glance

| | |
| --- | --- |
| Package | `Eigenverft.NetLib.Configuration.Binding` |
| Main API | `BindReplacingCollectionDefaults(...)` |
| Collections | Mutable lists and dictionaries |
| Empty values | Keep defaults or replace with empty |
| Target frameworks | .NET 8 and .NET 10 |
| License | MIT |

## 📦 Installation

```shell
dotnet add package Eigenverft.NetLib.Configuration.Binding
```

## 🚀 Quick start

The built-in binder appends list items and merges dictionary entries when the target already has values. This extension clears a configured mutable collection first, then lets the framework bind and convert its values:

```csharp
using System.Collections.Generic;
using Eigenverft.NetLib.Configuration.Binding;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
FilterOptions options = new();
builder.Configuration.GetSection("FilterOptions")
    .BindReplacingCollectionDefaults(
        options,
        EmptyCollectionBehavior.UseCodeDefaults);

public sealed class FilterOptions
{
    public List<string> Allowed { get; set; } = new() { "default" };
    public Dictionary<string, int> Weights { get; set; } = new() { ["default"] = 1 };
}

```

For options registration, apply the same policy whenever the options infrastructure creates or reloads an instance:

```csharp
using Microsoft.Extensions.DependencyInjection;

builder.Services
    .AddOptions<FilterOptions>()
    .BindReplacingCollectionDefaults(
        "FilterOptions",
        EmptyCollectionBehavior.UseEmptyCollection);
```

## 🧭 Behavior and limits

| Configuration state | Result |
| --- | --- |
| Collection key is missing | Keep code defaults |
| Configured list or dictionary has entries | Replace the initialized collection |
| Configured collection is empty with `UseCodeDefaults` | Keep code defaults |
| Configured collection is empty with `UseEmptyCollection` | Replace with an empty collection |

The empty-collection policy is required on every call. The helpers cover initialized mutable `IList` and `IDictionary` properties; other property types keep native binder behavior. `ConfigurationKeyNameAttribute` aliases are honored. Binding, value conversion, `BinderOptions`, named options, and change-token behavior remain framework-owned.

These APIs use reflection and are annotated for dynamic-code and trimming requirements. Preserve the options members used for binding when publishing a trimmed application.

## 🎯 Target frameworks

The package targets `net8.0` and `net10.0`.

## 🔗 Project links

- [NuGet package](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Binding)
- [Package source](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/tree/main/src/prj/Eigenverft.NetLib.Configuration.Binding)
- [Solution guide](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/src/sln/Eigenverft.NetLib.Configuration.Binding/Readme.md)

## 📄 License

[MIT License](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)
