# Eigenverft.NetLib.Security.DataProtection

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.NetLib.Security.DataProtection?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Security.DataProtection) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.NetLib.Security.DataProtection?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Security.DataProtection) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.NetLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.NetLib.Security.DataProtection) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)

Adapt ASP.NET Core Data Protection to reversible string transforms and persisted configuration-value codecs.

## At a glance

| Need | API |
| --- | --- |
| Protect and unprotect strings | `AspNetDataProtectionStringTransforms.DataProtection(...)` |
| Create a persisted configuration codec | `AspNetDataProtectionConfigurationValueCodecs.DataProtection(...)` |
| Select the key ring and isolation context | Stable key directory, application name, and purpose |

## Quick start

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

Keep the key ring, application name, and purpose stable for as long as protected values must remain available. The configuration-codec API additionally integrates with Configuration.Values and Hosting.DirectoryLayout. Data Protection does not add machine binding; an available key ring plus the same application name and purpose is sufficient across machines. It is separate from the certificate lifecycle used by WebLib's [Kestrel.Sni](https://www.nuget.org/packages/Eigenverft.WebLib.Kestrel.Sni). See the [NuGet README](../../prj/Eigenverft.NetLib.Security.DataProtection/NugetAssets/Readme.md) for key-ring lifecycle and cross-package details.

## Development

Run `dotnet test` from this directory to test the package for all target frameworks.
