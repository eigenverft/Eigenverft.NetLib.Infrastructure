# Eigenverft.NetLib.Transformations

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.NetLib.Transformations?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Transformations) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.NetLib.Transformations?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Transformations) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.NetLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.NetLib.Transformations) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)

Compose reusable reversible string transformations, from simple representations and obfuscation to password-based AES-GCM and machine-scoped protection.

## ✨ At a glance

| Capability | What it does | Starting point |
| --- | --- | --- |
| Reversible transforms | Apply and attempt to reverse a transform; failed reversal preserves the input | `ReversibleStringTransform` |
| Built-in transforms | Use Base64, Base92JsonSafe, ROT13, Caesar, DPAPI LocalMachine, password AES-GCM, or machine-bound AES | `ReversibleStringTransforms` |
| Composition | Apply transforms in order and reverse them in the opposite order | `ReversibleStringTransforms.Compose(...)` |
| Base92 encoding | Encode arbitrary bytes into a canonical JSON-string-safe representation | `Base92JsonSafeEncoder` |

The package also references [Eigenverft.NetLib.Security.MachineBinding](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/tree/main/src/prj/Eigenverft.NetLib.Security.MachineBinding) for the machine-bound transform. For self-describing persisted configuration values, use [Eigenverft.NetLib.Configuration.Values](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Values).

## 📦 Installation

```shell
dotnet add package Eigenverft.NetLib.Transformations
```

## 🚀 Quick start

Compose transforms in forward order. This example changes representation; it does not encrypt the text. For persisted configuration values that need a self-describing codec, see [Configuration.Values](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Values); raw transforms do not frame values or choose configuration keys.

```csharp
using System;
using System.IO;
using Eigenverft.NetLib.Transformations;

ReversibleStringTransform pipeline = ReversibleStringTransforms.Compose(
    ReversibleStringTransforms.Rot13,
    ReversibleStringTransforms.Base64);

string payload = pipeline.Apply("Hello");
if (!pipeline.TryReverse(payload, out string original))
{
    throw new InvalidDataException("The payload could not be reversed.");
}

Console.WriteLine(original);
```

`Compose(...)` applies first-to-last and reverses last-to-first. `TryReverse(...)` returns `false` and the original transformed value if any stage fails; no partial reversal is returned.

## 🔁 Available transforms and behavior

`ReversibleStringTransforms` provides `Base64`, `Base92JsonSafe`, `Rot13`, `Caesar(shift)`, `DpapiMachine`, `DpapiMachineBase64Url`, `AesPassword(password)`, `AesPassword(passwordAsciiBytes)`, `PhysicalMachineBoundAes()`, and `Compose(...)`. You can also construct a `ReversibleStringTransform` with custom apply/reverse delegates.

Transforms operate on string values only. They do not add persisted `enc:` wrappers, identify the transform during reversal, select configuration keys, or migrate stored values. For persisted configuration, [Configuration.Values](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Values) supplies self-describing codecs on top of these transforms.

## 🔐 Security and platform notes

- Base64 and Base92 are representations; ROT13 and Caesar are reversible obfuscation. None provides confidentiality.
- `Base92JsonSafe` avoids JSON-string escaping, but is not HTML/XML escaping; its alphabet includes characters such as `<`, `>`, `&`, apostrophe, and slash.
- `AesPassword(...)` uses AES-GCM with a password-derived key. Security depends on how the application obtains and protects the password. The transform retains the password for its lifetime; embedding it in an application is not a secrecy boundary. The byte-array overload only makes simple static inspection less direct.
- `DpapiMachine` and `DpapiMachineBase64Url` require Windows DPAPI LocalMachine and throw `PlatformNotSupportedException` on other platforms. They bind to the machine, not to a user or administrator boundary; another user on that Windows machine may unprotect the value.
- `PhysicalMachineBoundAes()` derives its password material from a platform fingerprint. It is lightweight resistance to copying application files to another machine, not a hardware-backed secret; source-machine access can reproduce the fingerprint.

## 🎯 Target frameworks

- `net8.0`
- `net10.0`

A .NET 9 consumer can use the compatible `net8.0` asset.

## 🔗 Project links

- [Source project](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/tree/main/src/prj/Eigenverft.NetLib.Transformations)
- [Solution guide](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/tree/main/src/sln/Eigenverft.NetLib.Transformations)
- [Configuration.Values package](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Values)
- [MachineBinding source project](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/tree/main/src/prj/Eigenverft.NetLib.Security.MachineBinding)
- [Repository](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure)

## 📄 License

[MIT License](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)
