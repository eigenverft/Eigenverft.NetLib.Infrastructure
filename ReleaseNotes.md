# Release notes

## Unreleased

- `b4327db` — WP2 adds host-agnostic IP normalization and CIDR matching with parsed-network/list-match caches, plus one shared configuration-binding primitive for collection defaults. The legacy RequestFilters project remains unchanged.

- `22646ee` — Review cleanup narrows collection-default replacement to initialized mutable lists/dictionaries, characterizes native binder merge/empty behavior, and exposes IP normalization/CIDR matching through small `IPAddress` extension APIs while preserving the CIDR caches.
- `da0e4e0` — Uses the same `BindReplacingCollectionDefaults(...)` name for direct configuration binding and `OptionsBuilder`, keeping A5/A6 as one public concept.
- Unreleased — `BindReplacingCollectionDefaults(...)` now requires an explicit `EmptyCollectionBehavior` on every call. Populated configured collections still replace code defaults and missing keys still keep them; each feature now declares whether `[]` / `{}` means `UseCodeDefaults` or an intentional `UseEmptyCollection`. `IOptionsMonitor` reload/change-token behavior remains framework-owned and is covered for both policies.
