# Eigenverft.NetLib.Security.Certificates

[![NuGet Version](https://img.shields.io/nuget/v/Eigenverft.NetLib.Security.Certificates?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Security.Certificates) [![NuGet Downloads](https://img.shields.io/nuget/dt/Eigenverft.NetLib.Security.Certificates?label=Downloads&logo=nuget)](https://www.nuget.org/packages/Eigenverft.NetLib.Security.Certificates) [![Repository CI](https://img.shields.io/github/actions/workflow/status/eigenverft/Eigenverft.NetLib.Infrastructure/cicd.yml?branch=main&label=repository%20CI)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/actions/workflows/cicd.yml) [![Targets](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4?logo=dotnet&logoColor=white)](https://www.nuget.org/packages/Eigenverft.NetLib.Security.Certificates) [![License](https://img.shields.io/badge/license-MIT-blue.svg?logo=mit)](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)

Create self-signed X.509 certificates and load PFX files with an explicit, policy-controlled recovery behavior.

## ✨ At a glance

| Capability | What it does | Starting point |
| --- | --- | --- |
| Self-signed certificates | Create certificates for TLS, code signing, or email protection with RSA or ECDSA profiles | `SelfSignedCertificateFactory.Create(...)` |
| Managed PFX files | Load a valid certificate or return a generated recovery certificate | `ManagedCertificateFile.LoadOrCreate(...)` |
| Recovery policy | Control whether recovery material is kept in memory or persisted | `CertificateRecoveryMode` |

## 📦 Installation

```shell
dotnet add package Eigenverft.NetLib.Security.Certificates
```

## 🚀 Quick start

Load a managed certificate and generate a replacement only when the PFX is missing or expired:

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

`ManagedCertificateResult.Certificate` is caller-owned and must be disposed. Use `SelfSignedCertificateFactory.Create(...)` directly when the application does not need a managed-file lifecycle.

## 🔐 Certificate and recovery behavior

`SelfSignedCertificateFactory` supports TLS server/client, code-signing, and email-protection purposes. Profiles include RSA 2048/3072 with SHA-256 and ECDSA P-256/SHA-256 or P-384/SHA-384. TLS server certificates require at least one DNS or IP subject alternative name.

`PreserveExisting` is the default recovery mode: a generated certificate is returned in memory, and the configured PFX path is not created or replaced. `ReplaceExpired` creates a missing PFX and replaces only an existing, successfully imported expired PFX. `ReplaceAnyUnusable` can overwrite existing files that fail import, password, read, or access checks, so it must not be used casually with externally managed credentials. `None` disables recovery and requires an existing, currently valid PFX with a private key.

Self-signed certificates are not automatically trusted by clients. Protect the PFX path and password using the application's deployment controls; this library does not configure filesystem ACLs or establish a certificate trust chain.

## 🔗 Related packages

The [WebLib Kestrel.Sni](https://www.nuget.org/packages/Eigenverft.WebLib.Kestrel.Sni) package consumes these certificate helpers for configuration-driven SNI certificates; this package does not depend on Kestrel or ASP.NET Core. [Hosting.DirectoryLayout](https://www.nuget.org/packages/Eigenverft.NetLib.Hosting.DirectoryLayout) offers a conventional `AppCerts` path, but certificate loading accepts an explicit file path. Machine binding and Data Protection are separate value/key-ring mechanisms: neither is applied automatically to PFX files by this package.

## 🎯 Target frameworks

- `net8.0`
- `net10.0`

## 🔗 Project links

- [Source project](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/tree/main/src/prj/Eigenverft.NetLib.Security.Certificates)
- [Solution documentation](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/tree/main/src/sln/Eigenverft.NetLib.Security.Certificates)
- [Repository](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure)

## 📄 License

[MIT](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/blob/main/LICENSE)
