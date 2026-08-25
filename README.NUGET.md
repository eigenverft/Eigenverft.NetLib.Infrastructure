# 🧱 Eigenverft.NetLib.Infrastructure

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.NetLib.Infrastructure?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Infrastructure) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.NetLib.Infrastructure?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Infrastructure) [![Build Status](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.NetLib.Infrastructure/cicd.yml?branch=main&label=build)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-.NET%208%20%7C%2010-512BD4?logo=dotnet&logoColor=white)](#-target-frameworks) [![License](https://img.shields.io/github/license/eigenverft/Eigenverft.NetLib.Infrastructure?logo=mit)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)

Host-independent operational infrastructure for .NET applications and Generic Host-based services.

NetLib provides predictable writable storage plus safe loading, validation, protection, reload, and
coordination of operational configuration. It keeps bad JSON candidates away from live settings,
switches complete application-defined profiles, and supplies reusable certificate, diagnostics, and
bootstrap primitives.

---

## ✨ At a glance

| Capability | Problem solved | Starting point |
| --- | --- | --- |
| Application directories | Predictable writable storage below the executable | `AddDefaultDirectoryLayout()` |
| SwitchableJson | Last-known-good JSON loading and safe reloads | `AddSwitchableJsonFile(...)` |
| Configuration Sets | Coordinated multi-file profiles | `AddConfigurationSet(...)` |
| Value preparation and protection | Validate, transform, or protect persisted values before publication | `SwitchableJsonRegistrationOptions` |
| Certificates and diagnostics | Managed certificate recovery, configuration provenance, and bootstrap logging | Public certificate and hosting helpers |
| Early host environment | Resolve the host environment before Generic Host or ASP.NET Core builder creation | `StaticHostEnvironment.EnvironmentName` |

## 📦 Installation

```shell
dotnet add package Eigenverft.NetLib.Infrastructure
```

Or with the NuGet Package Manager:

```powershell
Install-Package Eigenverft.NetLib.Infrastructure
```

## 🚀 Quick start

### Create the host foundation

```csharp
using Eigenverft.NetLib.Infrastructure.Hosting.DirectoryLayout;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder =
    HostApplicationBuilderFactory.CreateWithDefaultDirectory();
IAppDirectoryLayout directories = builder.GetDirectoryLayout();

string settingsDirectory =
    directories[DefaultDirectory.ApplicationSettings];

Console.WriteLine(settingsDirectory);

using IHost host = builder.Build();
await host.RunAsync();
```

### Add last-known-good JSON reloads
using IHost host = builder.Build();
await host.RunAsync();
```

### Read the host environment before creating the builder

```csharp
using Eigenverft.NetLib.Infrastructure.Hosting;

string bootstrapSettings =
    $"BootstrapLogger.{StaticHostEnvironment.EnvironmentName}.json";

bool development = StaticHostEnvironment.IsDevelopment;
bool customQa = StaticHostEnvironment.IsEnvironment("QA");
```

`StaticHostEnvironment` supports both Generic Host and ASP.NET Core startup conventions. Precedence is
process command-line arguments, then `DOTNET_ENVIRONMENT`, then `ASPNETCORE_ENVIRONMENT`, with
`Production` as the default. A normal Generic Host application simply skips the ASP.NET Core fallback
when that variable is absent. Custom environment names are preserved, and the value is captured once
at first type initialization.

### Add last-known-good JSON reloads

```csharp
using Eigenverft.NetLib.Infrastructure.Hosting.Configuration.SwitchableJson;
using Eigenverft.NetLib.Infrastructure.Hosting.DirectoryLayout;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = HostApplicationBuilderFactory.CreateWithDefaultDirectory();
IAppDirectoryLayout directories = builder.GetDirectoryLayout();

string operationalSettings = Path.Combine(
    directories[DefaultDirectory.ApplicationSettings],
    "OperationalSettings.json");

builder.AddSwitchableJsonFile(
    name: "OperationalSettings",
    initialPath: operationalSettings,
    optional: false,
    reloadOnChange: true);

using IHost host = builder.Build();
await host.RunAsync();
```

The required initial file must exist. Invalid later edits are rejected and the previous
configuration snapshot stays active.

## Application directory layout

The standard layout is created directly below the executable directory:

```text
<application>/
├─ AppLogs/
├─ AppData/
├─ AppState/
├─ AppProtectionKeys/
├─ AppCerts/
└─ AppSettings/
```

Each directory is created during registration and checked for write access, so path or permission problems fail early during startup.

The same layout is registered as `IAppDirectoryLayout` after `Build()`, so normal constructor injection works as expected:

```csharp
public sealed class Worker(
    ILogger<Worker> logger,
    IAppDirectoryLayout directories) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Using application data directory {Directory}",
            directories[DefaultDirectory.ApplicationData]);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
```

## 🗂️ Override standard folder names

```csharp
builder.AddDefaultDirectoryLayout(
    new Dictionary<DefaultDirectory, string>
    {
        [DefaultDirectory.ApplicationData] = "Data",
        [DefaultDirectory.ApplicationLogFiles] = "Logs",
    });
```

Unspecified standard directories retain their defaults.

## 🧩 Custom directory layouts

```csharp
builder.AddDirectoryLayout(
    new Dictionary<string, string>
    {
        ["Cache"] = "cache",
        ["Imports"] = "incoming",
    });

string imports = builder.GetDirectoryLayout()["Imports"];
```

Folder mappings are intentionally direct children of the application root. Rooted paths, nested paths, and traversal patterns are rejected.

## 🔧 Without Generic Host

```csharp
AppDirectoryLayout directories = AppDirectoryLayoutFactory.CreateDefault();
```

An explicit root can also be supplied to the factory for tools, tests, or custom bootstrap scenarios.

## Safe operational configuration

SwitchableJson prepares a changed or alternative JSON file before publication and keeps the
last-known-good snapshot when the candidate is missing, invalid, or rejected. Configuration Sets
coordinate several switchable sources under one application-defined value, preventing related
sources from silently using different profiles. Typical sets include operational modes,
proxy behavior, build generations, feature collections, environments, and deployment lanes.
Applications decide when to switch and may keep that choice transient or persist it as desired
state.

| Use case | Example values | Sources changed together |
| --- | --- | --- |
| Reverse-proxy topology | `Primary`, `Canary`, `Failover` | Routes, clusters, and health policy |
| Operational observability | `Normal`, `Verbose`, `Incident` | Logging, diagnostics, and tracing |
| Traffic and download limits | `Restricted`, `Normal`, `Burst` | Rate limits, concurrency, bandwidth, and size limits |
| Resilience policy | `Normal`, `Degraded`, `Emergency` | Timeouts, retries, circuit breakers, and fallbacks |
| Feature or release set | `Stable`, `Preview`, `Rollback` | Features, endpoint exposure, and UI capabilities |
| Application availability | `Open`, `ReadOnly`, `Maintenance` | Endpoint access, write policy, jobs, and maintenance responses |
| Asset or content set | `Current`, `Campaign`, `Legacy` | Asset manifests, templates, branding, and content paths |
| Backend integration topology | `Primary`, `Secondary`, `Offline` | Service endpoints, queue targets, and credential references |
| Retention and data lifecycle | `Short`, `Standard`, `Archive` | Retention periods, cleanup windows, and archive policy |
| Capacity and performance | `Economy`, `Balanced`, `Peak` | Concurrency, batching, caching, and background-work limits |

These are application-defined policies. Runtime switching requires reload-aware consumers;
startup-fixed behavior should be controlled through the desired-state store with
`ConfigurationSetApplyMode.StartupOnly` rather than a direct runtime switch.

For example, a reverse proxy can keep complete primary and failover generations side by side:

```text
AppSettings/Routing/
├── Primary/Routes.json
├── Primary/Clusters.json
├── Failover/Routes.json
└── Failover/Clusters.json
```

Register both files as one choice so a bad route or cluster candidate cannot publish half a
generation:

```csharp
using Eigenverft.NetLib.Infrastructure.Hosting.Configuration.ConfigurationSets;
using Eigenverft.NetLib.Infrastructure.Hosting.Configuration.SwitchableJson;

string routingRoot = Path.Combine(
    directories[DefaultDirectory.ApplicationSettings],
    "Routing");

SwitchableJsonRegistrationOptions routingSourceOptions = new()
{
    // Follow valid edits within whichever routing generation is active.
    ReloadOnChange = true,
};

builder
    .AddConfigurationSet(
        // Logical identity used by runtime and desired-state operations.
        name: "RoutingProfile",
        // Start with AppSettings/Routing/Primary/*.json.
        initialValue: "Primary",
        // Permit a deliberate transition to the reviewed fallback generation.
        additionalAllowedValues: ["Failover"])
    .AddSwitchableJson(
        // Resolve <root>/<value>/<fileName> for both participants.
        rootPath: routingRoot,
        options: routingSourceOptions,
        fileNames: ["Routes.json", "Clusters.json"]);
```

Both candidates are prepared first; if either is missing or invalid, `Primary` remains active.
Connect an admin UI or automation through an application-owned DI service:

```csharp
using Eigenverft.NetLib.Infrastructure.Hosting.Configuration.ConfigurationSets;
using Eigenverft.NetLib.Infrastructure.Hosting.DirectoryLayout;
using Microsoft.Extensions.DependencyInjection;

string profileStateFile = Path.Combine(
    directories[DefaultDirectory.ApplicationState],
    "ConfigurationSets.json");

// Register persistent desired-state control in addition to ephemeral runtime control.
builder.AddConfigurationSetStateFile(path: profileStateFile);
builder.Services.AddSingleton<RoutingProfileService>();

public sealed class RoutingProfileService(
    IConfigurationSetManager configurationSets,
    IConfigurationSetDesiredStateStore desiredState)
{
    // Change only the running process.
    public bool TrySwitchCurrentProcess(
        string value,
        out ConfigurationSetSwitchResult? result) =>
        configurationSets.TrySwitchRuntime(
            setName: "RoutingProfile", value: value, result: out result);

    // Persist the operator's selection and honor the configured apply mode.
    public ConfigurationSetStateApplyResult SetDesiredProfile(string value) =>
        desiredState.TrySetDesiredValue(
            setName: "RoutingProfile", value: value);
}
```

The same service shape works for traffic limits, resilience, maintenance, feature, or logging
profiles. An admin controller may inject it, display the active, desired, and allowed values from
`IConfigurationSetDesiredStateStore.GetDesiredStateStatus()`, and translate a reviewed UI action
into a switch. NetLib coordinates and reports the transition; the application remains responsible
for authentication, authorization, and audit logging.

### Preferred API

The public configuration surface is intentionally centered on developer-facing contracts and registration helpers:

- `builder.AddConfigurationSet(...)` returns a `ConfigurationSetRegistration` for fluent startup binding. External runtime control uses `IConfigurationSetManager.TrySwitchRuntime(...)`; set-specific control can use keyed `IConfigurationSetCoordinator.TrySwitch(...)`.
- `builder.AddSwitchableJsonFile(...)` registers a source; runtime control uses keyed `ISwitchableJsonConfiguration`.
- `IConfigurationSetCoordinator.BindSwitchableJson(...)` is an advanced binding API for already existing runtimes and is supported only for coordinators created by NetLib configuration-set registration; it is not the runtime switch API.
- `SwitchableJsonRegistrationOptions.CandidatePreparation` accepts `IJsonConfigurationSourcePreparation`; common preparations come from `JsonConfigurationCandidatePreparations`.
- `ConfigurationValueCodecs` provides the built-in persisted codecs. External adapters can compose a public `ReversibleStringTransform` with `new ConfigurationValueCodec(...)` and then use `JsonConfigurationCandidatePreparations.Decode(...)`.
- `ConfigurationValueRecovery.RecoverProtectedValues(...)` is an intentionally experimental recovery/debug helper that returns clear-text runtime values selected by registered NetLib `ValueProtection` rules. Copied configuration can be recovered elsewhere when the same non-host-bound protection context is available; unresolved protected envelopes throw with the configured codec name and guidance to run recovery on the original application server when host-bound state may be required. Using it requires explicit suppression of `EVFRECOVERY001`, and temporary recovery calls should be removed when finished.
- `ResetToMinimalConfigurationSources(...)` and `LogConfigurationResolution(...)` are Generic Host configuration utilities and work through `IHostApplicationBuilder`.

Concrete coordinator, provider, runtime, pipeline, watcher, and persistence-format implementation types are intentionally internal. They are created and exposed through the public contracts above and are not required for normal consumer code.

## Certificates

`Eigenverft.NetLib.Infrastructure.Security.Certificates` provides host-independent X.509 helpers:

- `SelfSignedCertificateFactory.Create(...)` creates caller-owned self-signed certificates for TLS server/client, code-signing, and email-protection purposes using RSA or ECDSA profiles.
- `ManagedCertificateFile.LoadOrCreate(...)` loads a managed PFX or returns a policy-controlled recovery certificate. `CertificateRecoveryMode.PreserveExisting` is the safe default and does not overwrite an existing unusable PFX.

The certificate APIs have no ASP.NET Core, Kestrel, SNI, configuration, or logging dependency.

## 🎯 Target frameworks

The package ships dedicated assets for:

- `net8.0`
- `net10.0`

A .NET 9 consumer can use the compatible `net8.0` asset.

## 🔗 Project links

- [GitHub repository](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure)
- [Documentation](https://eigenverft.github.io/Eigenverft.NetLib.Infrastructure/)
- [Issues](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/issues)
- [NuGet package](https://www.nuget.org/packages/Eigenverft.NetLib.Infrastructure)

## 📄 License

Licensed under the [MIT License](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE) by Eigenverft.

---

Made with ❤️ by Eigenverft
