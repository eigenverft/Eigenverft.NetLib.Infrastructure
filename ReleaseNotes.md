# Release notes

## Unreleased

- `b4327db` — WP2 adds host-agnostic IP normalization and CIDR matching with parsed-network/list-match caches, plus one shared configuration-binding primitive for collection defaults so missing keys keep defaults while present populated or empty collections replace them. The legacy RequestFilters project remains unchanged.

- `22646ee` — Review cleanup narrows collection-default replacement to initialized mutable lists/dictionaries, characterizes native binder merge/empty behavior, and exposes IP normalization/CIDR matching through small `IPAddress` extension APIs while preserving the CIDR caches.
