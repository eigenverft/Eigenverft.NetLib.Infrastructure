# Eigenverft.NetLib.Transformations

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.NetLib.Transformations?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Transformations) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.NetLib.Transformations?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Transformations) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.NetLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.NetLib.Transformations) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)

Apply reusable reversible transforms to strings, or compose them into a pipeline. The library stays persistence-neutral: it does not frame values or select configuration keys. See the [package guide](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/tree/main/src/prj/Eigenverft.NetLib.Transformations/NugetAssets/Readme.md) for API details, platform behavior, and security caveats.

| Capability | Entry point |
| --- | --- |
| Apply or reverse a transform | `ReversibleStringTransform` |
| Built-in transforms and ordered composition | `ReversibleStringTransforms` |
| Self-describing persisted configuration values | [Configuration.Values](https://www.nuget.org/packages/Eigenverft.NetLib.Configuration.Values) |

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

Composition applies first-to-last and reverses in the opposite order. `TryReverse(...)` returns `false` with the transformed input unchanged if any stage fails; no partial reversal is returned. Base64 and ROT13 change representation, not confidentiality. For persisted configuration values that need a self-describing codec, see [Configuration.Values](../Eigenverft.NetLib.Configuration.Values/Readme.md); raw transforms do not frame values or choose configuration keys.

## Development

Run package tests for both target frameworks from this directory:

```bash
dotnet test
```
