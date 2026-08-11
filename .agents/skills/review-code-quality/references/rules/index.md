# Rule Index

This file is the authoritative mapping from normalized, lowercase reviewable file extensions to rule files in this directory.

## Always Applicable

- `common.md`

## Extension Mapping

| Extensions | Rule files |
| --- | --- |
| `.cs`, `.csx` | `csharp.md` |
| `.csproj` | `csproj.md` |
| `.ps1`, `.psm1` | `powershell.md` |
| `.sln` | `sln.md` |
| `.slnx` | `slnx.md` |

Each selected mapped rule file may be a stub with no reportable rules or may contain one or more reportable stable-ID rules using the rule package contract in `SKILL.md`. Review every selected rule file. Use `common.md` for shared processing and report only concrete violations.
