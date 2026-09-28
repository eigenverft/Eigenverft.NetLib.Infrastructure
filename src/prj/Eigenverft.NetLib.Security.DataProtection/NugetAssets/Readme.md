# Eigenverft.NetLib.Security.DataProtection

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.NetLib.Security.DataProtection?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Security.DataProtection) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.NetLib.Security.DataProtection?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Security.DataProtection) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.NetLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.NetLib.Security.DataProtection) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)

ASP.NET Core Data Protection adapters for reversible string transforms and persisted configuration-value codecs.

## ✨ At a glance

| Capability | What it does | Starting point |
| --- | --- | --- |
| Reversible string protection | Protect and unprotect values with a persistent ASP.NET Core Data Protection key ring | `AspNetDataProtectionStringTransforms.DataProtection(...)` |
| Configuration values | Create a self-describing codec for persisted protected values | `AspNetDataProtectionConfigurationValueCodecs.DataProtection(...)` |
| Purpose isolation | Separate protected values by stable application name and purpose | `applicationName`, `purpose` |

## 📦 Installation

```shell
dotnet add package Eigenverft.NetLib.Security.DataProtection
```

## 🚀 Quick start

Create a transform with a persistent key-ring directory and stable isolation values:

```csharp
using System;
using System.IO;
using Eigenverft.NetLib.Security.DataProtection.Transformations;
using Eigenverft.NetLib.Transformations;

ReversibleStringTransform transform = AspNetDataProtectionStringTransforms.DataProtection(
    Path.Combine(AppContext.BaseDirectory, "AppProtectionKeys"),
    applicationName: "Worker",
    purpose: "configuration-values");

string protectedValue = transform.Apply("persisted value");
if (!transform.TryReverse(protectedValue, out string clearText))
{
    throw new InvalidOperationException("The value could not be unprotected.");
}
```

For configuration-value persistence, use `AspNetDataProtectionConfigurationValueCodecs.DataProtection(...)` to create a `ConfigurationValueCodec`. Its directory-layout overload uses `DefaultDirectory.ApplicationProtectionKeys` and the process entry assembly name; its explicit overload accepts the key path and application name directly.

## 🔑 Key-ring and package dependencies

The key ring is durable state. Back up the complete ring with the values that depend on it, retain old keys for as long as those values may be used, and protect the files with deployment-appropriate filesystem permissions. This adapter does not configure an additional at-rest encryptor; directory separation alone is not an access-control boundary. If a previously used installation unexpectedly points at a new or empty ring, existing protected values may become unavailable.

Keep `applicationName` and `purpose` stable while values must remain reversible. Changing either creates an isolation context that cannot unprotect values from the previous one. Data Protection does not add machine binding: another machine with the same key ring and isolation context can unprotect the values.

The configuration codec integrates with [Eigenverft.NetLib.Configuration.Values](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Values) and uses [Eigenverft.NetLib.Hosting.DirectoryLayout](https://www.nuget.org/packages/Eigenverft.NetLib.Hosting.DirectoryLayout) for its standard key-ring path. The transform API builds on [Eigenverft.NetLib.Transformations](https://www.nuget.org/packages/Eigenverft.NetLib.Transformations); these dependencies are declared by the package.

Data Protection is distinct from physical-machine binding: the same ring, application name, and purpose can be used on another machine, while `PhysicalMachineBoundAes()` is a separate transform exposed by [Transformations](https://www.nuget.org/packages/Eigenverft.NetLib.Transformations) and a convenience codec in Configuration.Values. This package does not manage TLS certificates; WebLib's [Kestrel.Sni](https://www.nuget.org/packages/Eigenverft.WebLib.Kestrel.Sni) uses the separate [Security.Certificates](https://www.nuget.org/packages/Eigenverft.NetLib.Security.Certificates) package.

## 🎯 Target frameworks

- `net8.0`
- `net10.0`

## 🔗 Project links

- [Source project](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/tree/main/src/prj/Eigenverft.NetLib.Security.DataProtection)
- [Solution documentation](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/tree/main/src/sln/Eigenverft.NetLib.Security.DataProtection)
- [Repository](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure)

## 📄 License

[MIT](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)
