# Eigenverft.NetLib.Security.Certificates

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.NetLib.Security.Certificates?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Security.Certificates) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.NetLib.Security.Certificates?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Security.Certificates) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.NetLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.NetLib.Security.Certificates) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)

Create self-signed X.509 certificates and load PFX files with an explicit recovery policy.

## At a glance

| Need | API |
| --- | --- |
| Create a standalone self-signed certificate | `SelfSignedCertificateFactory.Create(...)` |
| Load or recover a managed PFX | `ManagedCertificateFile.LoadOrCreate(...)` |
| Choose whether recovery can write to disk | `CertificateRecoveryMode` |

## Quick start

```csharp
using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using Eigenverft.NetLib.Security.Certificates;

string password = Environment.GetEnvironmentVariable("APP_PFX_PASSWORD")
    ?? throw new InvalidOperationException("APP_PFX_PASSWORD is required.");

ManagedCertificateResult managed = ManagedCertificateFile.LoadOrCreate(
    new ManagedCertificateFileOptions
    {
        FilePath = Path.Combine(AppContext.BaseDirectory, "certs", "worker.pfx"),
        Password = password,
        RecoveryMode = CertificateRecoveryMode.ReplaceExpired,
        Replacement = new SelfSignedCertificateOptions
        {
            Subject = new CertificateSubject { CommonName = "worker.example" },
            Purpose = CertificatePurpose.TlsServer,
            DnsNames = new[] { "worker.example" },
        },
    });

using X509Certificate2 certificate = managed.Certificate;
Console.WriteLine($"{managed.Action}; persisted: {managed.Persisted}");
```

The returned certificate is caller-owned and must be disposed. `ReplaceExpired` creates a missing PFX and replaces an existing one only if it imports successfully and is expired. TLS server certificates require at least one DNS or IP subject alternative name. WebLib's separate [Kestrel.Sni](https://www.nuget.org/packages/Eigenverft.WebLib.Kestrel.Sni) package consumes these certificate primitives; this package itself remains host-agnostic. See the [NuGet README](../../prj/Eigenverft.NetLib.Security.Certificates/NugetAssets/Readme.md) for other recovery modes and certificate caveats.

## Development

Run `dotnet test` from this directory to test the package for all target frameworks.
