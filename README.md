# 🧱 Eigenverft.NetLib.Infrastructure

<!-- Maintenance note: Keep README.NUGET.md aligned with this README for shared prose, examples, headings, badges, and feature descriptions. Use absolute NuGet/GitHub URLs there where this README can use repository-relative links; otherwise keep shared content in sync. -->

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.NetLib.Infrastructure?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Infrastructure) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.NetLib.Infrastructure?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Infrastructure) [![Build Status](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.NetLib.Infrastructure/cicd.yml?branch=main&label=build)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-.NET%208%20%7C%2010-512BD4?logo=dotnet&logoColor=white)](#-target-frameworks) [![License](https://img.shields.io/github/license/eigenverft/Eigenverft.NetLib.Infrastructure?logo=mit)](LICENSE)

Host-independent operational infrastructure for .NET applications and Generic Host-based services.

NetLib gives an application predictable writable storage and a safe way to load, validate, protect,
reload, and coordinate operational configuration. It is useful when a bad JSON edit must not replace
live settings, several files must move to one reviewed profile together, or certificates and startup
diagnostics must be available before the normal host lifecycle is ready.

---

## ✨ At a glance

| Capability | Problem solved | Starting point |
| --- | --- | --- |
| Application directories | Create and validate one predictable writable layout below the executable | `builder.AddDefaultDirectoryLayout()` |
| SwitchableJson | Reject missing, invalid, or unprepared JSON candidates while retaining last-known-good values | `builder.AddSwitchableJsonFile(...)` |
| Configuration Sets | Switch several related sources under one application-defined value | `builder.AddConfigurationSet(...)` |
| Value preparation and protection | Decode, normalize, migrate, validate, or protect selected persisted values before publication | `SwitchableJsonRegistrationOptions` and `ConfigurationValueCodecs` |
| Certificates and machine binding | Create or recover managed certificates and bind selected data to a deployment machine | `ManagedCertificateFile` and `PhysicalMachineBinding` |
| Startup diagnostics | Explain configuration precedence and log before the host is built | `LogConfigurationResolution(...)` and `BootstrapLogger<T>` |
| Early host environment | Resolve the host environment before a Generic Host or ASP.NET Core builder exists | `StaticHostEnvironment.EnvironmentName` |

The package targets .NET 8 and .NET 10 and is licensed under MIT.

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

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.AddDefaultDirectoryLayout();

IAppDirectoryLayout directories = builder.GetDirectoryLayout();

string settingsDirectory =
    directories[DefaultDirectory.ApplicationSettings];

Console.WriteLine(settingsDirectory);

using IHost host = builder.Build();
await host.RunAsync();
```

For the shortest setup, the factory is a shorthand for the same two calls:

```csharp
HostApplicationBuilder builder =
    HostApplicationBuilderFactory.CreateWithDefaultDirectory(args);
```

### Read the host environment before creating the builder

`StaticHostEnvironment` exposes the process-level host environment early enough for bootstrap work
that must happen before a Generic Host or ASP.NET Core builder and DI exist:

```csharp
using Eigenverft.NetLib.Infrastructure.Hosting;

string bootstrapSettings =
    $"BootstrapLogger.{StaticHostEnvironment.EnvironmentName}.json";

if (StaticHostEnvironment.IsDevelopment)
{
    Console.WriteLine("Development bootstrap profile selected.");
}
```

Resolution supports both Generic Host and ASP.NET Core startup conventions. Precedence is process
command-line arguments, then `DOTNET_ENVIRONMENT`, then `ASPNETCORE_ENVIRONMENT`, and finally
`Production` when none is set. A normal Generic Host application therefore behaves as usual when
`ASPNETCORE_ENVIRONMENT` is absent, while an ASP.NET Core application can use its conventional
fallback without requiring a second WebLib-specific resolver. Arbitrary names are preserved, so
`StaticHostEnvironment.IsEnvironment("QA")` works for custom environments as well. The value is
captured once when `StaticHostEnvironment` is first initialized, matching the startup-oriented nature
of the host environment.

### Add last-known-good JSON reloads

Register an existing operational JSON file through SwitchableJson when an invalid edit must not
replace the configuration currently used by the application:

```csharp
using Eigenverft.NetLib.Infrastructure.Hosting.Configuration.SwitchableJson;
using Eigenverft.NetLib.Infrastructure.Hosting.DirectoryLayout;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.AddDefaultDirectoryLayout();

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

The required initial file must exist. Later valid edits are published through ordinary
`IConfiguration`; a missing or invalid edit is rejected and the previous snapshot remains active.

## 📁 Application directory layout

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

Each directory is created during registration and checked for write access, so path or permission problems fail early during startup instead of surfacing later during normal application work.

The same layout is registered as `IAppDirectoryLayout` when the host is built, so normal constructor injection works as expected:

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

Unspecified standard directories keep their defaults:

```csharp
builder.AddDefaultDirectoryLayout(
    new Dictionary<DefaultDirectory, string>
    {
        [DefaultDirectory.ApplicationData] = "Data",
        [DefaultDirectory.ApplicationLogFiles] = "Logs",
    });
```

## 🧩 Custom directory layouts

Use semantic keys when the standard set is not what your application needs:

```csharp
builder.AddDirectoryLayout(
    new Dictionary<string, string>
    {
        ["Cache"] = "cache",
        ["Imports"] = "incoming",
    });

IAppDirectoryLayout directories = builder.GetDirectoryLayout();
string imports = directories["Imports"];
```

Folder mappings are intentionally direct children of the application root. Rooted paths, nested paths, and traversal patterns are rejected.

## 🔧 Without Generic Host

The same layout primitive can be used directly when no host builder is involved:

```csharp
AppDirectoryLayout directories = AppDirectoryLayoutFactory.CreateDefault();
```

An explicit root can also be supplied to the factory for tools, tests, or custom bootstrap scenarios.

## 🔄 Safe operational configuration

Use SwitchableJson when a changed or alternative JSON file must be loaded and prepared before it is
allowed to replace live configuration. A rejected file leaves the last-known-good snapshot active.
A Configuration Set adds the next level: several related files become one coordinated
application-level choice, so related logging, resilience, diagnostics, reverse-proxy routing, or
feature settings cannot silently end up on different profile values.

Typical problem domains include the following. Set values, file names, and combinations remain
entirely application-defined:

| Use case | Example values | Sources changed together |
| --- | --- | --- |
| Reverse-proxy topology | `Primary`, `Canary`, `Failover` | Routes, clusters, and health policy |
| Operational observability | `Normal`, `Verbose`, `Incident` | Logging, diagnostics, and tracing |
| Traffic and download limits | `Restricted`, `Normal`, `Burst` | Rate limits, download concurrency, bandwidth, and size limits |
| Resilience policy | `Normal`, `Degraded`, `Emergency` | Timeouts, retries, circuit breakers, and fallback behavior |
| Feature or release set | `Stable`, `Preview`, `Rollback` | Features, endpoint exposure, and UI capabilities |
| Application availability | `Open`, `ReadOnly`, `Maintenance` | Endpoint access, write policy, jobs, and maintenance responses |
| Asset or content set | `Current`, `Campaign`, `Legacy` | Asset manifests, templates, branding, and content paths |
| Backend integration topology | `Primary`, `Secondary`, `Offline` | Service endpoints, queue targets, and credential references |
| Retention and data lifecycle | `Short`, `Standard`, `Archive` | Retention periods, cleanup windows, and archive policy |
| Capacity and performance | `Economy`, `Balanced`, `Peak` | Concurrency, batching, caching, and background-work limits |

These are policy examples, not built-in meanings. NetLib publishes the selected values through
`IConfiguration`; runtime changes require reload-aware consumers such as `IOptionsMonitor<T>`,
application middleware, a reload-aware proxy, or a hosted service. Listener ports, the DI graph, and
middleware composition remain startup concerns; represent those through the desired-state store with
`StartupOnly` instead of requesting a direct runtime switch.

Consider an application that normally logs concise operational information, but needs more detail
and safer downstream behavior during an incident:

| Profile | `LoggerSettings.json` | `Resilience.json` | `Diagnostics.json` |
| --- | --- | --- | --- |
| `Normal` | Information | Normal retry and timeout policy | Expensive diagnostics off |
| `Degraded` | Warning | Reduced retries and shorter timeouts | Dependency diagnostics on |
| `Incident` | Debug | Incident-safe downstream policy | Detailed diagnostics on |

The directory overload follows `{rootPath}/{setValue}/{fileName}`. Store one complete, reviewed
combination below each profile directory:

```text
AppSettings/Operations/
├── Normal/LoggerSettings.json
├── Normal/Resilience.json
├── Normal/Diagnostics.json
├── Degraded/...
└── Incident/...
```

Register the three files as one coordinated application choice:

```csharp
// Resolve the package-managed settings directory and choose one root for all profile variants.
string settings = directories[DefaultDirectory.ApplicationSettings];
string operationsRoot = Path.Combine(settings, "Operations");

builder
    .AddConfigurationSet(
        // Logical identity used by runtime-manager and state-store operations.
        name: "OperationalProfile",
        // Value loaded initially when no persisted desired state overrides it.
        initialValue: "Normal",
        // Further values the application permits operators to select.
        additionalAllowedValues: ["Degraded", "Incident"])
    .AddSwitchableJson(
        // Resolve every participant as Operations/<value>/<fileName>.
        rootPath: operationsRoot,
        // Switch these three concerns together as one reviewed profile.
        fileNames: ["LoggerSettings.json", "Resilience.json", "Diagnostics.json"]);
```

`Normal` is the initial active value. NetLib does not decide that an incident has started. The
application or its control plane makes that policy decision and asks NetLib to apply it. For a
transient switch, an admin endpoint, health controller, or automation component can call:

```csharp
// Resolve the process-wide configuration-set control surface from DI.
IConfigurationSetManager profiles =
    host.Services.GetRequiredService<IConfigurationSetManager>();

// Request an ephemeral switch and retain its detailed completion result.
bool incidentActive = profiles.TrySwitchRuntime(
    // Select the logical set registered above.
    setName: "OperationalProfile",
    // Select one of that set's allowed values.
    value: "Incident",
    // Inspect this result when the switch is rejected.
    result: out ConfigurationSetSwitchResult? result);
```

The call returns only after every participant has prepared and the coordinated switch has completed.
If, for example, `Incident/LoggerSettings.json` is invalid, the request is rejected and the previous
complete profile remains active. Reload-aware logging and `IOptionsMonitor<T>` consumers then observe
the newly published values through the ordinary `IConfiguration` surface.

Use a desired-state file when the choice must survive process restarts or be controlled by an
operator rather than application code:

```csharp
// Store desired state separately from the profile files themselves.
string stateFile = Path.Combine(
    directories[DefaultDirectory.ApplicationState], "ConfigurationSets.json");

// Register, materialize, and watch the persistent control file.
IConfigurationSetStateStore profileState = builder.AddConfigurationSetStateFile(
    path: stateFile);
```

The first run creates a self-describing file with `Normal` as `DesiredValue`. Changing it to
`Incident`, or calling
`profileState.TrySetDesiredValue(setName: "OperationalProfile", value: "Incident")`, becomes the
persistent selector. The watcher applies runtime sets automatically. Mark a set with
`.ApplyMode(ConfigurationSetApplyMode.StartupOnly)` when a changed value should wait for restart.

### Switch one logical file using either layout

A set may contain only one participant. The profile values are entirely application-defined; the
following `Production`, `ProductionVerbose`, and `Development` values merely illustrate switching
one logical `LoggerSettings.json`.

#### Default directory convention

`LoggingProfile` is the logical set name used by runtime-manager and state-store operations; it is
intentionally not part of the file path. `Production` is the initial active value. With the default
`{root}/{value}/{fileName}` convention, the initial file is therefore
`AppSettings/Logging/Production/LoggerSettings.json`:

```text
AppSettings/Logging/
├── Production/LoggerSettings.json
├── ProductionVerbose/LoggerSettings.json
└── Development/LoggerSettings.json
```

```csharp
// The directory that contains one subdirectory per logging-profile value.
string loggingRoot = Path.Combine(settings, "Logging");

// Keep the startup-only fluent handle while binding this set's source file.
ConfigurationSetRegistration loggingProfile = builder.AddConfigurationSet(
    // Runtime and state-store identity; this is not a directory name.
    name: "LoggingProfile",
    // Load Logging/Production/LoggerSettings.json initially.
    initialValue: "Production",
    // Other application-defined values that may be selected later.
    additionalAllowedValues: ["ProductionVerbose", "Development"]);

// Use the default <root>/<value>/<fileName> path convention.
loggingProfile.AddSwitchableJson(
    rootPath: loggingRoot,
    fileName: "LoggerSettings.json");
```

#### Alternative suffix convention

To keep the variants in one directory instead, provide the path mapping explicitly:

```text
AppSettings/Logging/
├── LoggerSettings.Production.json
├── LoggerSettings.ProductionVerbose.json
└── LoggerSettings.Development.json
```

```csharp
// All logging variants live directly in this directory.
string loggingRoot = Path.Combine(settings, "Logging");

// Keep the startup-only fluent handle while binding the custom path resolver.
ConfigurationSetRegistration loggingProfile = builder.AddConfigurationSet(
    // Runtime and state-store identity; this is not part of the file name.
    name: "LoggingProfile",
    // Load LoggerSettings.Production.json initially.
    initialValue: "Production",
    // Values inserted into the file-name pattern by the resolver below.
    additionalAllowedValues: ["ProductionVerbose", "Development"]);

// Map each allowed value to Logging/LoggerSettings.<value>.json.
loggingProfile.AddSwitchableJson(
    sourcePathResolver: value =>
        Path.Combine(loggingRoot, $"LoggerSettings.{value}.json"));
```

#### Switch from application code

`IConfigurationSetManager` is available from DI and can be injected into a service, hosted service,
controller, or admin endpoint. The desired-state file registered above also exposes
`IConfigurationSetDesiredStateStore` through DI, so the application can deliberately offer both
temporary and persistent operations:

```csharp
using Eigenverft.NetLib.Infrastructure.Hosting.Configuration.ConfigurationSets;
using Microsoft.Extensions.DependencyInjection;

// Register the application-owned control service used by an admin API, UI backend, or automation.
builder.Services.AddSingleton<LoggingProfileService>();

public sealed class LoggingProfileService(
    IConfigurationSetManager configurationSets,
    IConfigurationSetDesiredStateStore desiredState)
{
    // Change only the current process; the next restart may select another desired value.
    public bool TrySwitchCurrentProcess(
        string value,
        out ConfigurationSetSwitchResult? result) =>
        configurationSets.TrySwitchRuntime(
            setName: "LoggingProfile", value: value, result: out result);

    // Persist the operator's selection and honor this set's Runtime or StartupOnly apply mode.
    public ConfigurationSetStateApplyResult SetDesiredProfile(string value) =>
        desiredState.TrySetDesiredValue(
            setName: "LoggingProfile", value: value);
}
```

An authenticated admin controller can inject this service and obtain `ActiveValue`, `DesiredValue`,
`AllowedValues`, consistency, and pending-restart state from
`desiredState.GetDesiredStateStatus()`. It can then translate a reviewed action such as “verbose
logging” into one of the calls above. The same pattern fits traffic limits, resilience, maintenance,
feature, or routing profiles. NetLib coordinates and reports the transition; authentication,
authorization, audit logging, and the policy deciding who may switch remain application
responsibilities.

#### Missing variants

With required sources, which is the default, a missing initial `LoggerSettings.Production.json`
fails registration. Inactive variants are not loaded during registration and may therefore be absent
without preventing startup. If `LoggerSettings.ProductionVerbose.json` is still missing when that
value is requested, the switch is rejected with
`ConfigurationSetSwitchFailureKind.ParticipantPreparationRejected`; the currently active value and
configuration remain unchanged. There is no implicit fallback to `Production`—it remains active only
when it was already active.

With one file, the set still provides an application-level name, allowed values, coordinated switch
results, lifecycle events, and optional persistent desired state. If none of those control-plane
semantics are needed, a standalone `AddSwitchableJsonFile(...)` is enough.

Routing often uses differently named files. Map each participant independently when the directory convention does not fit:

```csharp
// Keep one startup registration handle while binding both routing participants.
ConfigurationSetRegistration routing = builder.AddConfigurationSet(
    // Logical identity of this independent routing axis.
    name: "RoutingProfile",
    // Start with the primary routing files.
    initialValue: "Primary",
    // Permit controlled transitions to the other routing variants.
    additionalAllowedValues: ["Canary", "Failover"]);

routing
    .AddSwitchableJson(
        // Resolve the routes file for the requested value.
        sourcePathResolver: value => Path.Combine(
            settings, "Routing", $"routes-{value.ToLowerInvariant()}.json"))
    .AddSwitchableJson(
        // Resolve the matching clusters file for the same value.
        sourcePathResolver: value => Path.Combine(
            settings, "Routing", $"clusters-{value.ToLowerInvariant()}.json"));

// Alternative when every profile uses the same file names:
// routing.AddSwitchableJson(
//     rootPath: routingRoot,
//     fileNames: ["Routes.json", "Clusters.json"]);
```

Every mapped source is loaded before the coordinated value changes. A rejected candidate keeps the last fully coordinated profile active.

## 🛡️ Prepare configuration before it goes live

Candidate Preparation is a pre-publication pipeline over an isolated, parsed JSON snapshot:

```text
JSON file → parse → decode / normalize / validate → publish
```

### Why not just `AddJsonFile(...)`?

The built-in .NET JSON provider is the right default for a fixed `appsettings` file that only needs loading and optional reload-on-change. SwitchableJson adds value when configuration changes are operational events that must be prepared, accepted, rejected, switched, or coordinated deliberately.

| Concern | .NET `AddJsonFile(...)` | SwitchableJson and Configuration Sets |
| --- | --- | --- |
| Invalid changed file | No isolated candidate/commit boundary with a last-known-good guarantee | Parses and prepares first; rejection leaves the published snapshot untouched |
| Before publication | Loads parsed JSON values | Can decode, normalize, migrate, derive, or validate an isolated snapshot |
| Source identity | Uses the path registered at startup | Can prepare and atomically switch to another file without changing provider precedence |
| Several related files | Each provider reloads independently | A Configuration Set prepares every participant before committing the group |
| Operational feedback | Standard reload tokens and file-load exception handling | Typed results, failure kinds, lifecycle events, and active-source status |
| Protected file values | Requires a separate provider or application convention | Opt-in startup protection writes selected values and decodes them before publication |

Use the .NET default when those guarantees are unnecessary; use this package when a bad edit must not replace live settings or several operational concerns need one explicit acceptance boundary.

Preparations can decode protected values, migrate legacy keys, normalize endpoints, calculate derived settings, or reject invalid combinations. An application-owned preparation implements `IJsonConfigurationSourcePreparation`; throwing rejects the candidate before it becomes visible.

### Load one file without switching

A switchable source can simply replace an ordinary JSON source. No switch call or Configuration Set is required to gain safe reload with last-known-good data:

```csharp
// Resolve the managed settings directory and the one JSON file to load.
string settings = directories[DefaultDirectory.ApplicationSettings];
string partnerConfiguration = Path.Combine(settings, "PartnerApi.json");

// Register a keyed source that safely reloads this file when it changes.
builder.AddSwitchableJsonFile(
    // Stable identity used only when the runtime handle is requested from DI.
    name: "PartnerApi",
    // File active when the provider is created.
    initialPath: partnerConfiguration,
    // Watch accepted changes while preserving last-known-good data on failure.
    reloadOnChange: true);
```

The keyed runtime handle remains available if the application needs switching later; otherwise the source behaves like a normal reloadable configuration provider.

### Protect selected values independently

Configuration-value codecs do not depend on SwitchableJson or Configuration Sets. Build one reusable protection recipe, then encode only secret-bearing properties in provisioning or migration code:

```csharp
// Additional application/domain factor. Recoverable from the binary,
// so it complements but never replaces the deployment secret.
byte[] applicationFactor =
{
    0x23, 0x52, 0x66, 0x37, 0x5A, 0x39, 0x27, 0x27,
    0x5E, 0x52, 0x6C, 0x2E, 0x36, 0x49, 0x45, 0x4E,
    0x79, 0x4A, 0x52, 0x43, 0x4E, 0x4D, 0x3F, 0x5E,
    0x50, 0x5A, 0x6A, 0x5F, 0x4E, 0x32, 0x28, 0x4E,
};

// Actual secrecy depends on securely supplying and protecting this value.
string configurationProtectionSecret =
    Environment.GetEnvironmentVariable("APP_CONFIGURATION_PROTECTION_SECRET")
    ?? throw new InvalidOperationException(
        "APP_CONFIGURATION_PROTECTION_SECRET is required.");

ConfigurationValueCodec protectedValues = ConfigurationValueCodecs.Compose(
    codecs:
    [
        // Separate this application's values from another application using the same secret.
        ConfigurationValueCodecs.AesPassword(passwordAsciiBytes: applicationFactor),
        // Add the externally supplied secrecy factor.
        ConfigurationValueCodecs.AesPassword(password: configurationProtectionSecret),
        // Make the final envelope usable only with this machine fingerprint.
        ConfigurationValueCodecs.PhysicalMachineBoundAes(),
    ]);

static string CreatePartnerConfigurationJson(
    string endpoint,
    string apiToken,
    ConfigurationValueCodec protectedValues)
{
    return JsonSerializer.Serialize(new
    {
        PartnerApi = new
        {
            // Keep operational routing information readable.
            Endpoint = endpoint,
            // Persist only the composed envelope for the secret-bearing value.
            ApiToken = protectedValues.Encode(apiToken),
        },
    });
}
```

The resulting file mixes ordinary and protected values:

```json
{
  "PartnerApi": {
    "Endpoint": "https://api.example.com",
    "ApiToken": "enc:a3s6p1:<generated payload>"
  }
}
```

`Endpoint` remains readable while `ApiToken` receives the complete composed protection. Selection happens explicitly at the `Encode(...)` call, not by guessing from the JSON key. Apply it to API keys, access tokens, passwords, or client secrets rather than routes, endpoints, flags, or other ordinary configuration. The selected codecs and their order form an application-owned structural factor without storing the complete recipe as a password string. The embedded application factor adds a separate domain-specific layer. Both remain recoverable through code analysis and complement rather than replace the deployment secret. `TryDecode(...)` is available when the codec is used independently. When `PhysicalMachineBoundAes()` participates, provision the encoded value on its target machine.

### Combine both for transparent profile loading

Add `ValueProtection` when selected clear-text values should stay protected at rest while still being
published as clear text to the application. Protection is applied before startup load and re-applied
idempotently before runtime reloads and source switches. The following is startup registration code,
typically placed in `Program.cs`:

```csharp
// Resolve the package-managed settings directory and choose one root for all variants.
string settings = directories[DefaultDirectory.ApplicationSettings];
string operationsRoot = Path.Combine(settings, "Operations");

// Recoverable from the binary: useful as an application/domain factor,
// but not a secret boundary by itself.
byte[] applicationFactor =
{
    0x23, 0x52, 0x66, 0x37, 0x5A, 0x39, 0x27, 0x27,
    0x5E, 0x52, 0x6C, 0x2E, 0x36, 0x49, 0x45, 0x4E,
    0x79, 0x4A, 0x52, 0x43, 0x4E, 0x4D, 0x3F, 0x5E,
    0x50, 0x5A, 0x6A, 0x5F, 0x4E, 0x32, 0x28, 0x4E,
};

// Actual secrecy depends on securely supplying and protecting this value.
string configurationProtectionSecret =
    Environment.GetEnvironmentVariable("APP_CONFIGURATION_PROTECTION_SECRET")
    ?? throw new InvalidOperationException(
        "APP_CONFIGURATION_PROTECTION_SECRET is required.");

ConfigurationValueCodec protectedValues = ConfigurationValueCodecs.Compose(
    codecs:
    [
        // Separate this application's values from another application using the same secret.
        ConfigurationValueCodecs.AesPassword(passwordAsciiBytes: applicationFactor),
        // Add the externally supplied secrecy factor.
        ConfigurationValueCodecs.AesPassword(password: configurationProtectionSecret),
        // Bind the resulting envelope to this machine fingerprint.
        ConfigurationValueCodecs.PhysicalMachineBoundAes(),
    ]);

SwitchableJsonRegistrationOptions protectedSourceOptions = new()
{
    // Watch the file belonging to whichever profile is currently active.
    ReloadOnChange = true,
    // Protect only values whose final JSON key matches one of these patterns.
    ValueProtection = JsonConfigurationValueProtection.ForKeys(
        codec: protectedValues,
        patterns: ["*ApiKey*", "*Token*", "Password"]),

    // ValueProtection decodes first; application validation is optional:
    // CandidatePreparation = JsonConfigurationCandidatePreparations.From(
    //     "OperationalPolicy",
    //     new OperationalPolicyPreparation()),
};
```

The registration below expects the same four participants below every allowed value. Only
`ExternalServices.json` receives `protectedSourceOptions` and therefore protects matching values:

```text
AppSettings/Operations/
├── Normal/
│   ├── ExternalServices.json     ← matching values protected
│   ├── LoggerSettings.json
│   ├── Resilience.json
│   └── Diagnostics.json
├── Degraded/
│   ├── ExternalServices.json     ← matching values protected
│   ├── LoggerSettings.json
│   ├── Resilience.json
│   └── Diagnostics.json
└── Incident/
    ├── ExternalServices.json     ← matching values protected
    ├── LoggerSettings.json
    ├── Resilience.json
    └── Diagnostics.json
```

Bind those files to one coordinated operational choice:

```csharp
builder
    .AddConfigurationSet(
        // Logical identity used later by IConfigurationSetManager.
        name: "OperationalProfile",
        // Load the Normal directory before optional desired state is applied.
        initialValue: "Normal",
        // Accept only these additional operational modes.
        additionalAllowedValues: ["Degraded", "Incident"])
    .AddSwitchableJson(
        // Apply protection only to the secret-bearing participant.
        rootPath: operationsRoot,
        options: protectedSourceOptions,
        fileNames: ["ExternalServices.json"])
    .AddSwitchableJson(
        // Resolve the ordinary participants below the same profile directory.
        rootPath: operationsRoot,
        // Watch their active files without attaching ValueProtection.
        options: new SwitchableJsonRegistrationOptions
        {
            ReloadOnChange = true,
        },
        // Switch all three files together with ExternalServices.json.
        fileNames: ["LoggerSettings.json", "Resilience.json", "Diagnostics.json"]);
```

The fluent `ConfigurationSetRegistration` handle does not need to be retained or added to DI.
`AddConfigurationSet(...)` already registers the runtime infrastructure. Later, inject
`IConfigurationSetManager` into the service or controller that owns the policy decision and call
`TrySwitchRuntime(setName: "OperationalProfile", value: "Incident", result: out ...)`.

`ForKeys(...)` matches the final JSON key name regardless of nesting. Use `ForPaths(...)`, for
example `PartnerApi:*:ApiKey`, when the complete colon-separated configuration path should decide.

During registration, matching values in existing files are encoded before the provider and its watcher are created, so the startup write cannot trigger its own reload. Runtime loads apply the same protection policy again before reading a candidate: an externally edited clear-text value in the active file is re-protected on the next observed reload, and a clear-text value in an inactive variant is re-protected when that source is later loaded or switched to. Protection is load-bound rather than a continuous background invariant, writes changed JSON in formatted form under exclusive file access, and therefore requires write permission whenever matching clear text is present. The matching codec envelopes are then decoded before the optional `CandidatePreparation`, so application validation receives clear text while ordinary values pass through unchanged. A protection write is an at-rest side effect rather than part of the ConfigurationSet commit transaction; a later rejected switch does not roll it back. Missing files are not created by protection and retain the normal optional and switch-failure behavior.

## 🎛️ Control and observe desired state

Register a self-describing state file when operators or a control plane should persist the desired profile:

```csharp
// Keep desired-state control data outside the switchable profile directories.
string stateFile = Path.Combine(
    directories[DefaultDirectory.ApplicationState],
    "ConfigurationSets.json");

// Materialize and watch the file, and expose the same store through DI.
IConfigurationSetStateStore stateStore =
    builder.AddConfigurationSetStateFile(path: stateFile);

// Build the host only after every set and its state store have been registered.
using IHost host = builder.Build();

// Persist Degraded as the desired OperationalProfile value and apply it at runtime.
ConfigurationSetStateApplyResult applied = stateStore.TrySetDesiredValue(
    setName: "OperationalProfile",
    value: "Degraded");

// For a transient, non-persistent change, switch through the runtime manager instead.
// host.Services.GetRequiredService<IConfigurationSetManager>()
//     .TrySwitchRuntime(
//         setName: "OperationalProfile", value: "Normal", result: out _);
```

The state store can watch for changes, expose active-versus-desired drift, and report values waiting for restart. Mark restart-bound sets with `.ApplyMode(ConfigurationSetApplyMode.StartupOnly)`. `IConfigurationSetEventHub` provides process-wide or per-set completion notifications, while manager and store status snapshots expose consistency and participant state.

## 🔐 Understand composition

Composition is available at three deliberate layers:

| Need | API |
| --- | --- |
| Reversible in-memory string pipeline | `ReversibleStringTransforms.Compose(...)` |
| Self-describing persisted value pipeline | `ConfigurationValueCodecs.Compose(...)` |
| Ordered whole-candidate preparation pipeline | `JsonConfigurationCandidatePreparations.Compose(...)` |

Codecs encode from first to last and decode in reverse order. A failed composed decode returns the original persisted value unchanged. In the example, decoding requires the same application-owned composition, the embedded application factor, the deployment password, and the original machine identity. Composition is program behavior rather than a hard-coded recipe string; the embedded factor is an additional recoverable layer. The deployment password remains the secret factor and provides secrecy only when its delivery and storage are protected.

After successful Candidate Preparation, clear values are intentionally available to the running process through `IConfiguration`, options binding, and DI.

Use Windows-only `DpapiMachine`, cross-platform `AesPassword(...)`, lightweight `PhysicalMachineBoundAes()`, or a composition that matches the application's deployment model.

DPAPI LocalMachine is not a user or administrator boundary, and physical-machine binding is file-copy resistance rather than hardware-backed key storage. `Base64`, `Base92JsonSafe`, `Rot13`, and `Caesar(...)` are representations or analysis friction, not encryption.

## 🧪 Recover protected values

For explicit recovery or debugging, the running configuration can expose the clear-text values that NetLib value-protection rules decoded and published:

```csharp
#pragma warning disable EVFRECOVERY001 // Temporary recovery only.
var recovered = ConfigurationValueRecovery.RecoverProtectedValues(builder.Configuration);
#pragma warning restore EVFRECOVERY001
```

Inspect `recovered` in the debugger to see the affected full configuration paths and their current clear-text runtime values. A copied configuration can be recovered on a developer machine when the configured codec is not host-bound and the same required inputs, such as passwords or key material, are available. If a selected value still carries its persisted protection envelope, recovery throws instead of returning ciphertext; the exception includes the configured codec name and points out that machine-bound protection such as DPAPI LocalMachine or `PhysicalMachineBoundAes()` may require running the same bootstrap and recovery call on the original application server. The helper does not bypass protection or create a second decoding path; it inspects the values produced by the normal SwitchableJson load pipeline. `EVFRECOVERY001` is intentionally an experimental, build-stopping diagnostic until explicitly suppressed so temporary recovery code is hard to add accidentally and should be removed when the recovery session is finished.

## 🖥️ Read the machine fingerprint

```csharp
if (PhysicalMachineBinding.TryGetFingerprint(out string fingerprint))
{
    Console.WriteLine(fingerprint);
}
```

The fingerprint is stable machine information, not a secret. It uses the platform UUID on Windows, Linux, and macOS and can be unavailable on systems that do not expose a valid UUID.

## 🔏 Create or recover certificates

Load a valid PFX or create a usable self-signed replacement with an explicit recovery policy:

```csharp
string pfxPath = Path.Combine(
    directories[DefaultDirectory.ApplicationCerts],
    "worker.pfx");
string pfxPassword =
    Environment.GetEnvironmentVariable("APP_PFX_PASSWORD")
    ?? throw new InvalidOperationException("APP_PFX_PASSWORD is required.");

ManagedCertificateResult managed = ManagedCertificateFile.LoadOrCreate(
    new ManagedCertificateFileOptions
    {
        FilePath = pfxPath,
        Password = pfxPassword,
        Replacement = new SelfSignedCertificateOptions
        {
            Subject = new CertificateSubject
            {
                CommonName = "worker.example",
            },
            Purpose = CertificatePurpose.TlsServer,
            DnsNames = new[] { "worker.example" },
        },
    });

using X509Certificate2 certificate = managed.Certificate;
Console.WriteLine($"{managed.Action}; persisted: {managed.Persisted}");
```

`CertificateRecoveryMode.PreserveExisting` is the safe default: it creates a missing PFX but never overwrites an existing unusable credential. The result still provides an in-memory recovery certificate and reports load or persistence failures. Use `SelfSignedCertificateFactory.Create(...)` directly when no managed file lifecycle is needed.

## 🧩 Let configuration replace collection defaults

The native configuration binder intentionally mutates initialized collections: configured list items are appended and dictionary entries are merged with code defaults. That is useful for composition, but it does not implement the common options contract “missing key keeps defaults; present key replaces defaults; present empty collection clears defaults.” .NET 10 improves empty-array representation, but it still does not clear an already initialized mutable list automatically.

NetLib therefore uses one small binding concept for both lists and dictionaries instead of specialized collection wrapper types:

```csharp
using Eigenverft.NetLib.Infrastructure.Hosting.Configuration.CollectionOverrides;

public sealed class FilterOptions
{
    public List<string> Allowed { get; set; } = new() { "default" };
    public Dictionary<string, int> Weights { get; set; } = new() { ["default"] = 1 };
}

FilterOptions options = new();
configuration.GetSection("FilterOptions")
    .BindReplacingCollectionDefaults(options);

services
    .AddOptions<FilterOptions>()
    .BindReplacingCollectionDefaults("FilterOptions");
```

Only existing mutable list and dictionary properties whose configuration key is actually present are cleared before the framework binder runs. Missing keys leave code defaults untouched, populated collections replace defaults, and explicitly empty JSON arrays/objects become empty collections. Binding, `BinderOptions`, named options, and reload/change-token behavior remain framework-owned; other collection shapes keep the native binder semantics.

This is the A5/A6 decision: the legacy `OptionsConfigOverridesDefaultsList<T>` and `OptionsConfigOverridesDefaultsDictionary<TKey,TValue>` wrappers are **not** re-created in NetLib. Their shared intent is expressed once at the binding boundary.

## 🌐 Normalize and match IP networks

`Eigenverft.NetLib.Infrastructure.Networking` is host-agnostic and has no ASP.NET dependency:

```csharp
IPAddress canonical = IPAddress.Parse("::ffff:192.168.1.25").Normalize();
// 192.168.1.25

CidrNetwork network = CidrNetwork.Parse("192.168.1.123/24");
// normalized to 192.168.1.0/24

bool match = canonical.Matches(
    new[] { "10.0.0.0/8", "192.168.1.123/24" });
```

`IPAddress.Normalize()` maps IPv4-mapped IPv6 to IPv4 and removes IPv6 scope identifiers from canonical identity. `CidrNetwork` provides `Parse`/`TryParse` and `Contains`, accepts host-bit convenience input such as `192.168.1.123/24`, and normalizes it to the effective network. `IPAddress.Matches(...)` keeps the historical two cache layers internally: parsed-network results (including invalid parses) and repeated IP/list match results; list cache keys are order-independent and `*` remains match-all.

## 🔎 See which configuration source wins

```csharp
private static readonly ILogger StartupLogger = BootstrapLogger<Program>.CreateLogger();

public static async Task Main(string[] args)
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
    builder.Configuration.AddJsonFile("other-settings.json", optional: true, reloadOnChange: true);
    builder.LogConfigurationResolution(StartupLogger);
    using IHost host = builder.Build();
    await host.RunAsync();
}
```

Call `LogConfigurationResolution(...)` after registering the application's configuration sources and before `Build()`. It inspects the builder's current configuration-provider stack and writes the result through the supplied logger. It reports provider precedence and every complete key-shadowing chain:

```text
Config precedence (highest -> lowest): args -> envars -> json:appsettings.Production.json -> json:appsettings.json
Configuration key collisions found: 1.
Config key collision on PartnerApi:ApiKey; winner envars shadows json:appsettings.Production.json shadows json:appsettings.json
```

Only configuration key paths and provider origins are logged—never configuration values. This makes environment-variable overrides and accidentally shadowed JSON settings visible without dumping secrets.

## 🪵 Log startup before the host exists

```csharp
ILogger startupLogger =
    BootstrapLogger<Program>.CreateLogger(builder.Configuration);
```

The bootstrap logger works before the host and DI container exist. It uses an already initialized Serilog logger when available and otherwise falls back to Microsoft logging. `CreateRequiredSerilogLogger(...)` provides a strict, isolated JSON-configured Serilog bootstrap channel when fallback is not acceptable.

`ResetToMinimalConfigurationSources(...)` is available when an application intentionally wants to replace the default Generic Host sources; call it before adding custom providers because it clears the existing source collection.

Concrete providers, runtimes, watchers, and persistence formats remain internal; normal consumers use the registration helpers and public contracts shown above.

## 🎯 Target frameworks

The package ships dedicated assets for:

- `net8.0`
- `net10.0`

A .NET 9 consumer can use the compatible `net8.0` asset.

## 📚 Documentation

- [Guides and API reference](https://eigenverft.github.io/Eigenverft.NetLib.Infrastructure/docfx/production/)

## 🧪 Build and test

From the repository root:

```shell
dotnet build src/Eigenverft.NetLib.Infrastructure.slnx --configuration Release
dotnet test src/Eigenverft.NetLib.Infrastructure.slnx --configuration Release
```

## 🚢 Releases

`main` is the production channel. Every accepted change is built, tested, documented, packed, and published by the repository CI/CD workflow.

Package versions follow the Eigenverft Drydock timestamp-based versioning scheme. Published versions and download history are available on [NuGet.org](https://www.nuget.org/packages/Eigenverft.NetLib.Infrastructure).

## 🤝 Contributing and support

- 🐛 [Open an issue](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/issues)
- 🔧 [Submit a pull request](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/pulls)
- 📦 [View the package on NuGet.org](https://www.nuget.org/packages/Eigenverft.NetLib.Infrastructure)

## 📄 License

Licensed under the [MIT License](LICENSE) by Eigenverft.

---

<div align="center">
Made with ❤️ by Eigenverft
</div>
