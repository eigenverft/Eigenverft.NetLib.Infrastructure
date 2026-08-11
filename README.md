# Eigenverft.NetLib.Infrastructure

Private Eigenverft .NET infrastructure library for reusable, non-web-specific infrastructure concerns.

## Initial direction

This repository is intentionally starting small. It provides a separate home for infrastructure code that should not inherently depend on ASP.NET Core or other web-specific abstractions.

`Eigenverft.WebLib.Infrastructure` is a possible future source for selected transfers where functionality turns out to be generally useful outside web applications. No code is being moved or duplicated from that repository in this initial setup; candidates should be reviewed individually before transfer.

The repository follows the standard Eigenverft layout:

- `src/prj/Eigenverft.NetLib.Infrastructure` — library project
- `src/prj/Eigenverft.NetLib.Infrastructure.Tests` — tests
- `src/wrk` — temporary work/experiments

Initial target: .NET 10. Broader target-framework and package-publication decisions can be made once the first concrete consumers are known.
