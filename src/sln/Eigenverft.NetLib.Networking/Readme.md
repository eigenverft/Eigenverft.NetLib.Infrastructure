# Eigenverft.NetLib.Networking

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.NetLib.Networking?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Networking) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.NetLib.Networking?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Networking) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.NetLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.NetLib.Networking) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)

Host-independent IP address normalization, CIDR parsing, and cached IPv4/IPv6 matching. The package has no ASP.NET Core dependency.

| Package | Primary APIs | Target frameworks |
| --- | --- | --- |
| `Eigenverft.NetLib.Networking` | `Normalize()`, `CidrNetwork.Parse(...)`, `IPAddress.Matches(...)` | .NET 8, .NET 10 |

```csharp
using System;
using System.Net;
using Eigenverft.NetLib.Networking;

IPAddress canonical = IPAddress.Parse("::ffff:192.168.1.25").Normalize();
Console.WriteLine(canonical.ToCanonicalString());
// 192.168.1.25

CidrNetwork network = CidrNetwork.Parse("192.168.1.123/24");
// normalized to 192.168.1.0/24

bool contained = network.Contains(canonical);
bool allowed = canonical.Matches(
    new[] { "10.0.0.0/8", "192.168.1.123/24" });
```

IPv4-mapped IPv6 addresses use IPv4 matching semantics; CIDR host bits are normalized to the network. See the [NuGet package guide](../../prj/Eigenverft.NetLib.Networking/NugetAssets/Readme.md) for scope-ID and wildcard behavior.

## Development

Run package tests for both target frameworks from this directory:

```bash
dotnet test
```
