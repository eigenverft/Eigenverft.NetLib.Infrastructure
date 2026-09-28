# Eigenverft.NetLib.Security.MachineBinding

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.NetLib.Security.MachineBinding?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Security.MachineBinding) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.NetLib.Security.MachineBinding?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Security.MachineBinding) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.NetLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.NetLib.Security.MachineBinding) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)

Derive a stable, non-secret machine fingerprint from the platform UUID exposed by the operating system.

## At a glance

| Need | API |
| --- | --- |
| Try to get a fingerprint when a UUID is available | `PhysicalMachineBinding.TryGetFingerprint(...)` |
| Require a fingerprint or handle an error | `PhysicalMachineBinding.GetFingerprint()` |
| Read the normalized source UUID | `PhysicalMachineBinding.TryGetSystemPlatformUuid(...)` |

## Quick start

```csharp
using System;
using Eigenverft.NetLib.Security.MachineBinding;

if (PhysicalMachineBinding.TryGetFingerprint(out string fingerprint))
{
    Console.WriteLine(fingerprint);
}
else
{
    Console.WriteLine("No supported platform UUID is available.");
}
```

The fingerprint is machine information, not a secret or hardware-backed security boundary. Windows uses the SMBIOS system UUID, Linux the DMI product UUID, and macOS `IOPlatformUUID`; a virtual machine therefore binds to its hypervisor-exposed identity. The separate [Transformations](https://www.nuget.org/packages/Eigenverft.NetLib.Transformations) package uses it for `PhysicalMachineBoundAes()`, with a convenience codec also available from Configuration.Values; this package itself has no dependency on either. It does not bind Data Protection key rings or TLS certificates. See the [NuGet README](../../prj/Eigenverft.NetLib.Security.MachineBinding/NugetAssets/Readme.md) for support and security limits.

## Development

Run `dotnet test` from this directory to test the package for all target frameworks.
