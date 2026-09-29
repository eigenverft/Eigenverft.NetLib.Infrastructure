# Eigenverft.NetLib.Configuration.Values

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.NetLib.Configuration.Values?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Values) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.NetLib.Configuration.Values?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Values) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.NetLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Values) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)

Encode selected configuration values with reusable, self-describing codecs, or encode matching string values in an existing JSON file.

## ✨ At a glance

| Capability | What it does | Starting point |
| --- | --- | --- |
| Value codecs | Encode and decode individual persisted string values | `ConfigurationValueCodec` |
| Built-in transforms | Use Base64, Base92, ROT13, Caesar, DPAPI, AES-password, or machine-bound AES transforms | `ConfigurationValueCodecs` |
| Codec composition | Apply codecs in order and reverse that order when decoding | `ConfigurationValueCodecs.Compose(...)` |
| JSON file encoding | Encode matching string values selected by full configuration paths | `JsonConfigurationFileEncoder.EncodeMatchingValuesInPlace(...)` |

The package builds on [Eigenverft.NetLib.Transformations](https://www.nuget.org/packages/Eigenverft.NetLib.Transformations). It can be used independently of the configuration providers.

## 📦 Installation

```shell
dotnet add package Eigenverft.NetLib.Configuration.Values
```

## 🚀 Quick start

Create one codec from a secret supplied by your deployment and apply it only to the JSON path that should be encoded:

```csharp
using System;
using Eigenverft.NetLib.Configuration.Values;

string password = Environment.GetEnvironmentVariable("CONFIGURATION_PROTECTION_SECRET")
    ?? throw new InvalidOperationException("A protection secret is required.");
var codec = ConfigurationValueCodecs.AesPassword(password);

int changed = JsonConfigurationFileEncoder.EncodeMatchingValuesInPlace(
    "AppSettings/PartnerSettings.json",
    "PartnerApi:ApiToken",
    codec);
Console.WriteLine($"Encoded {changed} value(s).");
```

For independent use, `Encode(...)` returns a self-describing persisted value and `TryDecode(...)` attempts to recover the clear text:

```csharp
using System;
string token = Environment.GetEnvironmentVariable("PARTNER_API_TOKEN")
    ?? throw new InvalidOperationException("A partner API token is required.");
string persisted = codec.Encode(token);
if (!codec.TryDecode(persisted, out string clearText))
{
    throw new InvalidOperationException("The value was not encoded with this codec.");
}
```

## 🔐 Choose and compose codecs

`ConfigurationValueCodecs` provides `Base64`, `Base92JsonSafe`, `Rot13`, `Caesar(shift)`, `DpapiMachine`, `DpapiMachineBase64Url`, `AesPassword(...)`, and `PhysicalMachineBoundAes()`. You can also construct a codec around a public `ReversibleStringTransform`.

```csharp
var protectedValues = ConfigurationValueCodecs.Compose(
    ConfigurationValueCodecs.AesPassword(password),
    ConfigurationValueCodecs.PhysicalMachineBoundAes());

string persisted = protectedValues.Encode(token);
bool decoded = protectedValues.TryDecode(persisted, out string clearText);
```

Composition encodes from first to last and decodes in reverse. A failed decode returns the original persisted value through the `out` parameter. The same composition and factors are required to recover a value; values using machine-bound transforms must be provisioned on the target machine.

Base64, Base92, ROT13, and Caesar are representations or reversible transforms, not encryption. Passwords, key material, and any machine-binding assumptions remain the application's responsibility. DPAPI LocalMachine is Windows-specific and is not a user or administrator boundary.

## 🗂️ Encode selected JSON values

`EncodeMatchingValuesInPlace` matches case-insensitive glob patterns against complete, colon-separated configuration paths, including object and array indices. Only matching JSON strings are encoded; recognized NetLib envelopes are left alone, so repeating the operation is idempotent. JSON `null` is encoded as an empty string by default; pass `nullAsEmpty: false` to leave it unchanged.

The file is rewritten only if a value changes. In-place rewrites hold exclusive access for each read/transform/write cycle, retry brief Windows file-sharing conflicts, and re-read a newer external file state instead of restoring an older snapshot. A rewrite uses formatted JSON and removes comments, trailing commas, and original whitespace. Encoding clear-text values requires write access to the file. The helper does not load configuration or decode values while binding them.

To apply protection and decoding as configuration sources are loaded, use [Eigenverft.NetLib.Configuration.SwitchableJson](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.SwitchableJson), which builds on this package. This package itself remains independent of SwitchableJson and Configuration Sets.

## 🎯 Target frameworks

- `net8.0`
- `net10.0`

A .NET 9 consumer can use the compatible `net8.0` asset.

## 🔗 Project links

- [Source project](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/tree/main/src/prj/Eigenverft.NetLib.Configuration.Values)
- [Solution documentation](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/tree/main/src/sln/Eigenverft.NetLib.Configuration.Values)
- [Transformations package](https://www.nuget.org/packages/Eigenverft.NetLib.Transformations)
- [Repository](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure)

## 📄 License

[MIT](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)
