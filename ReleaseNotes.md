# Release notes

## Unreleased

- `984c9ec` — Define the SerilogRelay limits/outage policy model for durable spool capacity, intermittent clients, maximum batch wait, endpoint retry/circuit handling, poison-event isolation, bounded emergency memory, shutdown deadlines, observability, and explicit 1h/12h/1d/7d failure semantics; no runtime behavior changes yet.

- `b1acfad` — Increase the SerilogRelay bounded emergency Channel from 1024 to 16384 events, keep overflow regression coverage aligned with the larger bound, and make emergency-worker cancellation deterministic so the net10.0 suite remains stable at 100% line/branch/method coverage.

- `147b839`, `9ff5db1`, `5d8d9e3`, `4b00d6f`, `dff782b`, `171bc69`, `4e1324a`, `edb5a79`, `b3e5b0b`, `f3ec329` — Harden and finalize the pre-release SerilogRelay surface: correct retention/SelfLog/property JSON behavior, rename the public API to `SerilogRelaySink` / `.WriteTo.SerilogRelay(...)`, make disposal idempotent across sync/async callers, add protocol-v1 stable `EventId` delivery and retry-safe identity, clear the remaining Release-pack analyzer findings while keeping persisted rendered messages invariant across host cultures, simplify client setup to endpoint-first fire-and-forget defaults with automatic application/spool resolution and optional storage overrides, add corrupted-spool quarantine/recreate/report recovery for `SQLITE_CORRUPT` / `SQLITE_NOTADB`, persist per-event `ApplicationId`, pseudonymous hashed `MachineId`, and `ProcessId` for cross-application/same-host and parallel-process diagnostics, remove unreleased pre-v1 spool compatibility so the current v1 schema is the first supported format, replace inherited unconditional TLS certificate bypass with normal platform validation plus an explicit `dangerousAcceptAnyServerCertificate` opt-in, and resolve local-spool write loss with a bounded 1024-event emergency Channel that retries SQLite first and can rescue events directly over HTTP while leaving final file/memory limit policy for a separate design pass.

- `7f99054`, `310ce0b`, `5fe100a` — Add the `Eigenverft.NetLib.SerilogRelay` library scaffold in the current multi-library layout, migrate the prior SerilogCentralLoggingSink revival intent and placeholder stubs, use the current Eigenverft NuGet icon/metadata, and keep `REVIVAL.md` as solution-level repository documentation rather than NuGet package content.

- `b4327db` — WP2 adds host-agnostic IP normalization and CIDR matching with parsed-network/list-match caches, plus one shared configuration-binding primitive for collection defaults. The legacy RequestFilters project remains unchanged.

- `22646ee` — Review cleanup narrows collection-default replacement to initialized mutable lists/dictionaries, characterizes native binder merge/empty behavior, and exposes IP normalization/CIDR matching through small `IPAddress` extension APIs while preserving the CIDR caches.
- `da0e4e0` — Uses the same `BindReplacingCollectionDefaults(...)` name for direct configuration binding and `OptionsBuilder`, keeping A5/A6 as one public concept.
- `7405062` — `BindReplacingCollectionDefaults(...)` now requires an explicit `EmptyCollectionBehavior` on every call. Populated configured collections still replace code defaults and missing keys still keep them; each feature now declares whether `[]` / `{}` means `UseCodeDefaults` or an intentional `UseEmptyCollection`. `IOptionsMonitor` reload/change-token behavior remains framework-owned and is covered for both policies.

- `52b1b8a` - IP normalization now preserves IPv6 `ScopeId` for native IPv6 addresses while continuing to map IPv4-mapped IPv6 addresses to IPv4; `ToCanonicalString()` reflects the preserved scope identifier when present.

- d622abf - NuGet package metadata now uses the neutral on-all Eigenverft icon (evt-logo_on_all_standard_128x128.png), so the next production package also carries the corrected README.NUGET.md from main.
