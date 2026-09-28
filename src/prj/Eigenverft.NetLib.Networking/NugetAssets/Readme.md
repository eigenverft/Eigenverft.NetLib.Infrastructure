# Eigenverft.NetLib.Networking

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.NetLib.Networking?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Networking) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.NetLib.Networking?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Networking) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.NetLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.NetLib.Networking) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)

Host-independent IP address normalization, CIDR parsing, and cached address matching for IPv4 and IPv6.

## ✨ At a glance

| | |
| --- | --- |
| Package | `Eigenverft.NetLib.Networking` |
| APIs | `IPAddress.Normalize()`, `CidrNetwork`, `IPAddress.Matches(...)` |
| Framework dependency | Microsoft.Extensions.Caching.Memory |
| ASP.NET Core dependency | None |
| Target frameworks | .NET 8 and .NET 10 |
| License | MIT |

## 📦 Installation

```shell
dotnet add package Eigenverft.NetLib.Networking
```

## 🚀 Quick start

Normalize IPv4-mapped IPv6 addresses, parse a network, and match an address against one CIDR or a list:

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

## 🧭 Parsing and matching behavior

`Normalize()` maps IPv4-mapped IPv6 addresses to IPv4. Native IPv6 addresses remain unchanged, including their `ScopeId`; `ToCanonicalString()` formats the normalized address while preserving a native IPv6 scope identifier.

`CidrNetwork.Parse(...)` and `TryParse(...)` accept both IPv4 and IPv6 CIDRs and clear host bits in the supplied address. For example, `192.168.1.123/24` becomes `192.168.1.0/24`. `Contains(...)` returns false for an address from the other address family.

`IPAddress.Matches(...)` accepts one CIDR or a collection. A literal `*` matches every address; empty and invalid entries do not match. Parsed CIDRs, including invalid text, and repeated address/list evaluations are cached internally. Collection cache keys are independent of entry order.

## 🎯 Target frameworks

The package targets `net8.0` and `net10.0`.

## 🔗 Project links

- [NuGet package](https://www.nuget.org/packages/Eigenverft.NetLib.Networking)
- [Package source](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/tree/main/src/prj/Eigenverft.NetLib.Networking)
- [Solution guide](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/src/sln/Eigenverft.NetLib.Networking/Readme.md)

## 📄 License

[MIT License](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)
