# Release notes

## Unreleased

- `147b839`, `9ff5db1`, `5d8d9e3` — Harden and finalize the pre-release SerilogRelay surface: correct retention/SelfLog/property JSON behavior, rename the public API to `SerilogRelaySink` / `.WriteTo.SerilogRelay(...)`, make disposal idempotent across sync/async callers, and add protocol-v1 stable `EventId` delivery with legacy-spool migration and retry-safe identity.

- `7f99054`, `310ce0b`, `5fe100a` — Add the `Eigenverft.NetLib.SerilogRelay` library scaffold in the current multi-library layout, migrate the prior SerilogCentralLoggingSink revival intent and placeholder stubs, use the current Eigenverft NuGet icon/metadata, and keep `REVIVAL.md` as solution-level repository documentation rather than NuGet package content.

- `b4327db` — WP2 adds host-agnostic IP normalization and CIDR matching with parsed-network/list-match caches, plus one shared configuration-binding primitive for collection defaults. The legacy RequestFilters project remains unchanged.

- `22646ee` — Review cleanup narrows collection-default replacement to initialized mutable lists/dictionaries, characterizes native binder merge/empty behavior, and exposes IP normalization/CIDR matching through small `IPAddress` extension APIs while preserving the CIDR caches.
- `da0e4e0` — Uses the same `BindReplacingCollectionDefaults(...)` name for direct configuration binding and `OptionsBuilder`, keeping A5/A6 as one public concept.
- `7405062` — `BindReplacingCollectionDefaults(...)` now requires an explicit `EmptyCollectionBehavior` on every call. Populated configured collections still replace code defaults and missing keys still keep them; each feature now declares whether `[]` / `{}` means `UseCodeDefaults` or an intentional `UseEmptyCollection`. `IOptionsMonitor` reload/change-token behavior remains framework-owned and is covered for both policies.

- `52b1b8a` - IP normalization now preserves IPv6 `ScopeId` for native IPv6 addresses while continuing to map IPv4-mapped IPv6 addresses to IPv4; `ToCanonicalString()` reflects the preserved scope identifier when present.

- d622abf - NuGet package metadata now uses the neutral on-all Eigenverft icon (evt-logo_on_all_standard_128x128.png), so the next production package also carries the corrected README.NUGET.md from main.
