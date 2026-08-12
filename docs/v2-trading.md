# v2 — Trading server (design intent, not scheduled)

v1 is local-only, but the data model already anticipates this: every stored Pokémon is a `PKH` with a unique HOME-style tracker.

## Goals

Fix what fans complain about in HOME's GTS, while staying fully self-hosted:

- **GTS-style listings with real filters**: shiny, IV range, ball, language, origin game, nature; filter by what the requester is *offering*, not just what they want.
- **Legitimacy flag**: PKHeX `LegalityAnalysis` result attached to every listing — transparent, user-filterable, never silently blocking.
- **Moderation**: per-server block lists and advertising-nickname filters (the Machamps.com problem).
- **Wonder trade** and **friend/direct trades** between instances.
- **Trade evolutions trigger** on completed trades (HOME doesn't do this).
- Federation: small servers peering with each other rather than one central service.

## Non-goals

- No live-console trading, no official HOME/GTS interop (ToS).
- No accounts economy, points, or microtransactions.

## Data model hooks already in place

- `StoredPokemon.Data` = serialized PKH (HOME's actual entity format) → listings can ship bytes directly.
- `StoredPokemon.HomeTracker` (unique nonzero ulong) → clone/dupe detection across a federation.
- Denormalized Species/Form/Shiny/Level columns → listing filters without deserializing blobs.

## Open questions (decide when v2 starts)

- Trust model between federated servers (web-of-trust vs explicit peering list).
- Trade atomicity across two self-hosted instances (two-phase commit vs escrow-by-server).
- Whether legality flags are computed by the listing server or attested by the client.
