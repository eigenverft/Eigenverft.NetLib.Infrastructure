# Eigenverft.NetLib.Hosting.SelfHttpWarmup

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.NetLib.Hosting.SelfHttpWarmup?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Hosting.SelfHttpWarmup) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.NetLib.Hosting.SelfHttpWarmup?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Hosting.SelfHttpWarmup) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.NetLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.NetLib.Hosting.SelfHttpWarmup) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)

Warm up an ASP.NET Core application by sending HTTP GET requests to its own endpoints once the host has started.

## ✨ At a glance

| Capability | What it does | Starting point |
| --- | --- | --- |
| Code-based warmup | Enable one or more absolute HTTP or HTTPS targets | `AddSelfHttpWarmup(...)` |
| Configuration-based warmup | Bind the `SelfHttpWarmup` options section | `AddSelfHttpWarmup()` |
| Startup timing | Set an optional initial delay and per-request timeout | `SelfHttpWarmupOptions` |
| Host lifecycle | Wait until the application reports started; cancel in-flight requests on shutdown | Hosted service |

## 📦 Installation

```shell
dotnet add package Eigenverft.NetLib.Hosting.SelfHttpWarmup
```

## 🚀 Quick start

Use the application's actual listening URL and a lightweight endpoint that is available after startup:

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

The hosted service waits for `ApplicationStarted`, then sends each configured target sequentially with `GET`. The default initial delay is zero and each request has a five-second timeout.

For a lightweight liveness target, the separate [Eigenverft.WebLib.HealthProbes](https://www.nuget.org/packages/Eigenverft.WebLib.HealthProbes) package provides a fixed GET/HEAD `/health` response: GET returns 200 with `OK`, and HEAD returns 200 without a body. It does not invoke ASP.NET Core health checks or report dependency readiness. Either package can be used without the other.

## ⚙️ Configure through appsettings

Register the feature and bind the `SelfHttpWarmup` section:

```csharp
builder.Services.AddSelfHttpWarmup();
```

```json
{
  "SelfHttpWarmup": {
    "Enabled": true,
    "InitialDelay": "00:00:01",
    "RequestTimeout": "00:00:05",
    "TargetUrls": [
      "http://localhost:5080/health"
    ]
  }
}
```

Options binding alone leaves warmup disabled. The URL overloads and the options-action overload enable it automatically. Targets must be absolute HTTP or HTTPS URLs; invalid targets supplied through the direct URL overload are rejected during registration.

The client connects directly without a proxy, does not follow redirects, and has a one-second connection timeout per resolved address. Point it at an address and port served by this process. Request failures and timeouts are logged and do not stop the host. The package depends on Microsoft.Extensions hosting and HTTP abstractions and does not add an ASP.NET Core shared-framework dependency.

## 🎯 Target frameworks

- `net8.0`
- `net10.0`

A .NET 9 consumer can use the compatible `net8.0` asset.

## 🔗 Project links

- [Source project](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/tree/main/src/prj/Eigenverft.NetLib.Hosting.SelfHttpWarmup)
- [Solution documentation](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/tree/main/src/sln/Eigenverft.NetLib.Hosting.SelfHttpWarmup)
- [Repository](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure)

## 📄 License

[MIT](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)
