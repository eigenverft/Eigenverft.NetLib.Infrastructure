# Eigenverft.NetLib.Hosting.SelfHttpWarmup

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.NetLib.Hosting.SelfHttpWarmup?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Hosting.SelfHttpWarmup) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.NetLib.Hosting.SelfHttpWarmup?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Hosting.SelfHttpWarmup) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.NetLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.NetLib.Hosting.SelfHttpWarmup) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)

Send sequential HTTP GET requests to the application's own endpoints after the host reports that it has started.

## At a glance

| Need | API |
| --- | --- |
| Configure endpoint warmup in code | `AddSelfHttpWarmup(...)` |
| Bind the `SelfHttpWarmup` configuration section | `AddSelfHttpWarmup()` |
| Set delay and request timeout | `SelfHttpWarmupOptions` |

## Quick start

```csharp
using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Eigenverft.NetLib.Hosting.SelfHttpWarmup;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5080");
builder.Services.AddSelfHttpWarmup(
    "http://localhost:5080/health",
    options =>
    {
        options.InitialDelay = TimeSpan.FromSeconds(1);
        options.RequestTimeout = TimeSpan.FromSeconds(3);
    });

var app = builder.Build();
app.MapGet("/health", () => Results.Ok());
await app.RunAsync();
```

Use the actual address and port served by this process. Warmup starts after the host reports started, then sends sequential GET requests; failures are logged and do not stop the host. The separate [WebLib.HealthProbes](https://www.nuget.org/packages/Eigenverft.WebLib.HealthProbes) package can provide a fixed `/health` liveness endpoint as a target, but it does not run ASP.NET Core health checks. See the [NuGet README](../../prj/Eigenverft.NetLib.Hosting.SelfHttpWarmup/NugetAssets/Readme.md) for configuration-only registration and lifecycle details.

## Development

Run `dotnet test` from this directory to test the package for all target frameworks.
