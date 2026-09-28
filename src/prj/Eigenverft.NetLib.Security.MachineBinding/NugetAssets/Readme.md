# Eigenverft.NetLib.Security.MachineBinding

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.NetLib.Security.MachineBinding?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Security.MachineBinding) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.NetLib.Security.MachineBinding?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Security.MachineBinding) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.NetLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.NetLib.Security.MachineBinding) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)

Read a supported system/platform UUID and derive a stable, non-secret fingerprint for lightweight machine binding.

## ✨ At a glance

| Capability | What it does | Starting point |
| --- | --- | --- |
| Platform identity | Read a normalized system UUID on Windows, Linux, or macOS | `TryGetSystemPlatformUuid(...)` |
| Machine fingerprint | Derive a versioned uppercase SHA-256 hexadecimal value | `TryGetFingerprint(...)` or `GetFingerprint()` |

## 📦 Installation

```shell
dotnet add package Eigenverft.NetLib.Security.MachineBinding
```

## 🚀 Quick start

Use the non-throwing API when the platform may not expose a valid UUID:

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

`GetFingerprint()` throws when the operating system is unsupported or no valid UUID is available. Windows uses the SMBIOS system UUID, Linux uses the DMI product UUID, and macOS uses `IOPlatformUUID`. The result is a 64-character uppercase SHA-256 hex string.

## 🛡️ Security boundary

The fingerprint and underlying platform UUID are machine information, not secret key material. This helper is not encryption or a hardware security boundary; it adds only a lightweight obstacle to copying application files to another machine. A process with access to the original platform UUID can reproduce the fingerprint.

Virtual machines generally bind to the UUID exposed by the hypervisor, so cloning or reprovisioning may preserve or change the value. This package has no dependency on other Eigenverft packages. The separate [Transformations](https://www.nuget.org/packages/Eigenverft.NetLib.Transformations) package consumes the fingerprint for `PhysicalMachineBoundAes()`; Configuration.Values exposes a convenience codec over that transform. Neither Data Protection key rings nor TLS certificates are automatically bound to this fingerprint, and WebLib's [Kestrel.Sni](https://www.nuget.org/packages/Eigenverft.WebLib.Kestrel.Sni) uses the separate [Security.Certificates](https://www.nuget.org/packages/Eigenverft.NetLib.Security.Certificates) package.

## 🎯 Target frameworks

- `net8.0`
- `net10.0`

## 🔗 Project links

- [Source project](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/tree/main/src/prj/Eigenverft.NetLib.Security.MachineBinding)
- [Solution documentation](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/tree/main/src/sln/Eigenverft.NetLib.Security.MachineBinding)
- [Repository](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure)

## 📄 License

[MIT](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)
