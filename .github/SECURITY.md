# Security policy

## Supported versions

Security fixes are provided for the latest package version published on NuGet.org. Please reproduce a suspected issue against that version when possible.

## Report a vulnerability

Please use [GitHub private vulnerability reporting](https://github.com/eigenverft/Eigenverft.NetLib.Infrastructure/security/advisories/new). Do not open a public issue for an undisclosed vulnerability.

Include the affected package version and API, target framework, operating system, impact, and a minimal reproduction if available. We will use the private report to confirm the finding and coordinate remediation and disclosure.

## Security boundaries

- Base64, Base92JsonSafe, ROT13, and Caesar codecs are representations, not encryption.
- DPAPI LocalMachine protection is Windows-only and is not a user or administrator privilege boundary.
- Physical machine binding uses a non-secret platform fingerprint for lightweight file-copy resistance; it is not hardware-backed key storage.
- Callers remain responsible for trusting, storing, rotating, and disposing certificates created or loaded by the certificate helpers.
