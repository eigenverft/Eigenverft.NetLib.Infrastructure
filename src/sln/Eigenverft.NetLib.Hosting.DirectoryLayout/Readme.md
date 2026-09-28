# Eigenverft.NetLib.Hosting.DirectoryLayout

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.NetLib.Hosting.DirectoryLayout?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Hosting.DirectoryLayout) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.NetLib.Hosting.DirectoryLayout?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Hosting.DirectoryLayout) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.NetLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.NetLib.Hosting.DirectoryLayout) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)

Create standard writable application folders below the executable directory and resolve the host environment before builder creation.

## At a glance

| Need | API |
| --- | --- |
| Register standard folders | `AddDefaultDirectoryLayout()` |
| Register application-defined folder keys | `AddDirectoryLayout(...)` |
| Retrieve a registered path | `IAppDirectoryLayout` |
| Read the early environment | `StaticHostEnvironment` |

## Quick start

```csharp
using System;
using Eigenverft.NetLib.Hosting.DirectoryLayout;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.AddDefaultDirectoryLayout();

IAppDirectoryLayout directories = builder.GetDirectoryLayout();
string settings = directories[DefaultDirectory.ApplicationSettings];
Console.WriteLine(settings);

using IHost host = builder.Build();
await host.RunAsync();
```

The default folders are direct children of `AppContext.BaseDirectory`; registration creates them and checks write access. For ASP.NET Core, the separate [WebLib.Hosting.DirectoryLayout](https://www.nuget.org/packages/Eigenverft.WebLib.Hosting.DirectoryLayout) package builds on this layout and adds the web content root, `wwwroot`, and a semantic `Web` directory mapping. See the [NuGet README](../../prj/Eigenverft.NetLib.Hosting.DirectoryLayout/NugetAssets/Readme.md) for folder overrides, custom keys, and environment precedence.

## Development

Run `dotnet test` from this directory to test the package for all target frameworks.
