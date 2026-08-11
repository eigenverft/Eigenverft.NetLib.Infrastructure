# Getting started

Install the package:

```shell
dotnet add package Eigenverft.NetLib.Infrastructure
```

The normal entry point is the Generic Host builder extension:

```csharp
using Eigenverft.NetLib.Infrastructure.Hosting.DirectoryLayout;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.AddDefaultDirectoryLayout();
IAppDirectoryLayout directories = builder.GetDirectoryLayout();

using IHost host = builder.Build();
await host.RunAsync();
```

The default layout is rooted at `AppContext.BaseDirectory` and creates `AppLogs`, `AppData`, `AppState`, `AppCerts`, and `AppSettings`. Every mapped directory is created and checked for write access during registration.

After `Build()`, the same layout is available as `IAppDirectoryLayout` through normal constructor injection.

## Custom names

```csharp
builder.AddDefaultDirectoryLayout(
    new Dictionary<DefaultDirectory, string>
    {
        [DefaultDirectory.ApplicationData] = "Data",
        [DefaultDirectory.ApplicationLogFiles] = "Logs",
    });
```

For a completely custom semantic layout, use `AddDirectoryLayout(...)`.

## Without Generic Host

```csharp
AppDirectoryLayout directories = AppDirectoryLayoutFactory.CreateDefault();
```

The factory also accepts an explicit root path for tools, tests, and custom bootstrap scenarios.
