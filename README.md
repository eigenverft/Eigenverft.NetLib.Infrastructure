# 🧱 Eigenverft.NetLib.Infrastructure

<!-- Maintenance note: This GitHub README has a NuGet/CommonMark counterpart in README.NUGET.md. Keep shared public-facing content aligned. -->

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.NetLib.Infrastructure?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Infrastructure) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.NetLib.Infrastructure?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Infrastructure) [![Build Status](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.NetLib.Infrastructure/cicd.yml?branch=main&label=build)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-.NET%208%20%7C%2010-512BD4?logo=dotnet&logoColor=white)](#-target-frameworks) [![License](https://img.shields.io/github/license/eigenverft/Eigenverft.NetLib.Infrastructure?logo=mit)](LICENSE)

Small, reusable infrastructure primitives for .NET applications and Generic Host-based services.

Provides predictable, executable-rooted application directories with automatic creation and writable validation.

Also includes Configuration Sets, SwitchableJson, configuration-value codecs and preparations, generic reversible string transforms, JSON-safe Base92 representation, machine binding, DPAPI machine-scope transforms, certificate primitives, configuration diagnostics, and pre-host bootstrap logging.

---

## ✨ At a glance

| | |
| --- | --- |
| Package | `Eigenverft.NetLib.Infrastructure` |
| Primary host integration | `builder.AddDefaultDirectoryLayout()` |
| Convenience builder | `HostApplicationBuilderFactory.CreateWithDefaultDirectory(args)` |
| Root | `AppContext.BaseDirectory` |
| Default folders | `AppLogs`, `AppData`, `AppState`, `AppProtectionKeys`, `AppCerts`, `AppSettings` |
| Host integration | Available before `Build()` and through DI afterwards |
| Also included | Runtime configuration, value codecs, machine binding, certificates, diagnostics, bootstrap logging |
| Target frameworks | .NET 8 and .NET 10 |
| License | MIT |

## 📦 Installation

```shell
dotnet add package Eigenverft.NetLib.Infrastructure
```

Or with the NuGet Package Manager:

```powershell
Install-Package Eigenverft.NetLib.Infrastructure
```

## 🚀 Quick start

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

## 🔄 Switch complete configuration profiles

A Configuration Set turns a technical collection of JSON files into one application-level choice:

| Set | Example values | Sources changed together |
| --- | --- | --- |
| Routing profile | `Primary`, `Canary`, `Failover` | Routes and clusters |
| Operational profile | `Normal`, `Degraded`, `Incident` | Features, resilience, and diagnostics |
| Feature or release bundle | `Stable`, `Beta` | A complete feature configuration, optionally on restart |
| Environment or tenant profile | Application-defined values | One or more profile-specific settings files |

The directory overload follows `{rootPath}/{setValue}/{fileName}`. This example switches three operational files as one coordinated group:

```csharp
string settings = directories[DefaultDirectory.ApplicationSettings];
string operationsRoot = Path.Combine(settings, "Operations");

builder
    .AddConfigurationSet(
        "OperationalProfile",
        "Normal",
        "Degraded",
        "Incident")
    .AddSwitchableJson(
        operationsRoot,
        "Features.json",
        "Resilience.json",
        "Diagnostics.json");
```

The example reads files such as `AppSettings/Operations/Normal/Features.json` and `AppSettings/Operations/Incident/Diagnostics.json`.

Routing often uses differently named files. Map each participant independently when the directory convention does not fit:

```csharp
ConfigurationSetRegistration routing = builder.AddConfigurationSet(
    "RoutingProfile",
    "Primary",
    "Canary",
    "Failover");

routing
    .AddSwitchableJson(value => Path.Combine(
        settings,
        "Routing",
        $"routes-{value.ToLowerInvariant()}.json"))
    .AddSwitchableJson(value => Path.Combine(
        settings,
        "Routing",
        $"clusters-{value.ToLowerInvariant()}.json"));

// Alternative when every profile uses the same file names:
// routing.AddSwitchableJson(routingRoot, "Routes.json", "Clusters.json");
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
string settings = directories[DefaultDirectory.ApplicationSettings];
string partnerConfiguration = Path.Combine(settings, "PartnerApi.json");

builder.AddSwitchableJsonFile(
    "PartnerApi",
    partnerConfiguration,
    reloadOnChange: true);
```

The keyed runtime handle remains available if the application needs switching later; otherwise the source behaves like a normal reloadable configuration provider.

### Protect selected values independently

Configuration-value codecs do not depend on SwitchableJson or Configuration Sets. Build one reusable protection recipe, then encode only secret-bearing properties in provisioning or migration code:

```csharp
// Additional application/domain factor. Recoverable from the binary,
// so it complements but never replaces the deployment secret.
const string embeddedApplicationFactor = "partner-api-v1";

// Actual secrecy depends on securely supplying and protecting this value.
string deploymentPassword =
    Environment.GetEnvironmentVariable("APP_CONFIGURATION_PASSWORD")
    ?? throw new InvalidOperationException(
        "APP_CONFIGURATION_PASSWORD is required.");

ConfigurationValueCodec protectedValues = ConfigurationValueCodecs.Compose(
    ConfigurationValueCodecs.AesPassword(embeddedApplicationFactor),
    ConfigurationValueCodecs.AesPassword(deploymentPassword),
    ConfigurationValueCodecs.PhysicalMachineBoundAes());

static string CreatePartnerConfigurationJson(
    string endpoint,
    string apiToken,
    ConfigurationValueCodec protectedValues)
{
    return JsonSerializer.Serialize(new
    {
        PartnerApi = new
        {
            Endpoint = endpoint,
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

Add `ValueProtection` when existing clear-text values should be protected automatically at startup and decoded before a profile becomes visible:

```csharp
string operationsRoot = Path.Combine(settings, "Operations");

// Recoverable from the binary: useful as an application/domain factor,
// but not a secret boundary by itself.
const string applicationFactor = "worker-operational-profile-v1";

// Actual secrecy depends on securely supplying and protecting this value.
string deploymentPassword =
    Environment.GetEnvironmentVariable("APP_CONFIGURATION_PASSWORD")
    ?? throw new InvalidOperationException(
        "APP_CONFIGURATION_PASSWORD is required.");

ConfigurationValueCodec protectedValues = ConfigurationValueCodecs.Compose(
    ConfigurationValueCodecs.AesPassword(applicationFactor),
    ConfigurationValueCodecs.AesPassword(deploymentPassword),
    ConfigurationValueCodecs.PhysicalMachineBoundAes());

var sourceOptions = new SwitchableJsonRegistrationOptions
{
    ReloadOnChange = true,
    ValueProtection = JsonConfigurationValueProtection.ForKeys(
        protectedValues,
        "*ApiKey*",
        "*Token*",
        "Password"),

    // ValueProtection decodes first; application validation is optional:
    // CandidatePreparation = JsonConfigurationCandidatePreparations.From(
    //     "OperationalPolicy",
    //     new OperationalPolicyPreparation()),
};

ConfigurationSetRegistration operationalProfile =
    builder.AddConfigurationSet(
        "OperationalProfile",
        "Normal",
        "Degraded",
        "Incident");

operationalProfile
    .AddSwitchableJson(
        operationsRoot,
        sourceOptions,
        "Features.json")
    .AddSwitchableJson(
        operationsRoot,
        "Resilience.json",
        "Diagnostics.json");
```

This protects matching values in `Features.json` for `Normal`, `Degraded`, and `Incident`; `Resilience.json` and `Diagnostics.json` remain outside that rule because they are registered by a separate call. `ForKeys(...)` matches the final JSON key name regardless of nesting. Use `ForPaths(...)`, for example `PartnerApi:*:ApiKey`, when the complete colon-separated configuration path should decide.

During registration, matching values in existing files are encoded once and changed JSON is atomically rewritten in formatted form. The provider and its watcher are created only afterwards, so the write cannot trigger its own reload. On initial load, reload, and profile switch, the matching codec envelopes are decoded before the optional `CandidatePreparation`; application validation therefore receives clear text. Ordinary values pass through unchanged. A later external clear-text edit is only read at runtime and is protected on the next process start. Missing files are not created by protection and retain the normal optional and switch-failure behavior.

## 🎛️ Control and observe desired state

Register a self-describing state file when operators or a control plane should persist the desired profile:

```csharp
string stateFile = Path.Combine(
    directories[DefaultDirectory.ApplicationState],
    "ConfigurationSets.json");

IConfigurationSetStateStore stateStore =
    builder.AddConfigurationSetStateFile(stateFile);

using IHost host = builder.Build();
ConfigurationSetStateApplyResult applied = stateStore.TrySetDesiredValue(
    "OperationalProfile",
    "Degraded");

// For transient, non-persistent control instead:
// host.Services.GetRequiredService<IConfigurationSetManager>()
//     .TrySwitchRuntime("OperationalProfile", "Normal", out _);
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

## 🧯 Recover protected values locally

For explicit local recovery or debugging, the running configuration can expose the clear-text values that NetLib value-protection rules already decoded and published:

```csharp
#pragma warning disable EVFRECOVERY001 // Temporary local recovery only.
var recovered = ConfigurationValueRecovery.RecoverProtectedValues(builder.Configuration);
#pragma warning restore EVFRECOVERY001
```

Inspect `recovered` in the debugger to see the affected full configuration paths and their current clear-text runtime values. The helper does not decode backing files or bypass protection; it reads only active SwitchableJson provider values selected by registered `ValueProtection` rules. `EVFRECOVERY001` is intentionally an experimental, build-stopping diagnostic until explicitly suppressed so temporary recovery code is hard to add accidentally and should be removed when the recovery session is finished.

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

## 🔎 See which configuration source wins

```csharp
ILogger startupLogger =
    BootstrapLogger<Program>.CreateLogger(builder.Configuration);

builder.LogConfigurationResolution(startupLogger);
```

Call `LogConfigurationResolution(...)` after registering configuration sources and before `Build()`. It reports provider precedence and every complete key-shadowing chain:

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
