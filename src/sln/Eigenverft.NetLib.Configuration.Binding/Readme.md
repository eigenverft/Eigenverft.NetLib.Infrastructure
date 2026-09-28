# Eigenverft.NetLib.Configuration.Binding

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.NetLib.Configuration.Binding?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Binding) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.NetLib.Configuration.Binding?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Binding) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.NetLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Binding) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)

Replace initialized list and dictionary defaults when configuration supplies those collections. Choose whether an explicitly empty value keeps the code defaults or clears them.

| Package | Primary API | Target frameworks |
| --- | --- | --- |
| `Eigenverft.NetLib.Configuration.Binding` | `BindReplacingCollectionDefaults(...)` | .NET 8, .NET 10 |

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

Missing keys keep defaults; populated collections replace them. For an explicit empty list or dictionary, pass `UseCodeDefaults` or `UseEmptyCollection`. See the [NuGet package guide](../../prj/Eigenverft.NetLib.Configuration.Binding/NugetAssets/Readme.md) for the options-registration form and binding limits.

## Development

Run package tests for both target frameworks from this directory:

```bash
dotnet test
```
