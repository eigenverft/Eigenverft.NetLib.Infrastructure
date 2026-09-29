# Eigenverft.NetLib.Configuration.Values

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.NetLib.Configuration.Values?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Values) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.NetLib.Configuration.Values?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Values) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.NetLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Values) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)

Encode selected persisted configuration values with self-describing codecs, or apply a codec to matching strings in an existing JSON file.

## At a glance

| Need | API |
| --- | --- |
| Encode or decode a persisted value | `ConfigurationValueCodec` |
| Select a built-in codec or compose codecs | `ConfigurationValueCodecs` |
| Encode matching JSON string values | `JsonConfigurationFileEncoder.EncodeMatchingValuesInPlace(...)` |

## Quick start

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

The encoder selects values by full colon-separated configuration path, leaves recognized encoded envelopes unchanged, and reports how many values changed. In-place rewrites use exclusive file access, retry brief Windows sharing conflicts, and re-read newer external file content instead of overwriting it from an older snapshot. Use the same codec and required factors when decoding. For JSON profiles loaded at startup or switched at runtime, the separate [SwitchableJson](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.SwitchableJson) package can apply a codec to selected keys while loading; Values itself does not load configuration. See the [NuGet README](../../prj/Eigenverft.NetLib.Configuration.Values/NugetAssets/Readme.md) for codec composition, matching, and security behavior.

## Development

Run `dotnet test` from this directory to test the package for all target frameworks.
