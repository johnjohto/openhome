# OpenHOME — Project Plan (approved 2026-08-12)

Open-source, self-hosted Pokémon HOME alternative built on PKHeX.Core.

## Confirmed decisions

- **Name**: OpenHOME — public repo `github.com/johnjohto/openhome`
- **Architecture**: self-hosted web app — ASP.NET Core 10 API + React/Vite/TS frontend (desktop + phone browser parity, no mobile/Switch feature split)
- **Scope v1**: local-only; data model designed for a self-hosted trade server in v2
- **Hacks/fangames**: both, phased — pokeemerald-expansion-style GBA hacks first (M4), Pokémon Essentials second (M5)
- **License**: GPL-3.0 (mandatory — PKHeX.Core is GPLv3)
- **PKHeX.Core**: pinned to 26.7.7; upgrade deliberately (monthly upstream churn with breaking changes)

## Feature mapping (HOME parity + fixes)

| HOME feature | OpenHOME |
|---|---|
| 200 boxes / 6000 Pokémon (Premium) | Unlimited boxes, free, self-hosted |
| Hostage mechanic on downgrade | Does not exist — it's your disk |
| Reads all console saves (Switch app) | Save library: upload/register saves, browse boxes without the "cartridge" |
| Cloud backup of Pokémon only | Full save-file backup with versioned history (fills the NSO-exclusion gap) |
| Two-way transfers, per-game movesets | `PKH` round-trip transfers via PKHeX.Core, per-game payloads preserved |
| One-way locks (GO, LGPE, Z-A, BDSP locks) | Optional strict mode (official rules) vs default free mode + legality warning |
| National/regional dex | Living-dex tracker: national + per-save regional, forms/shinies |
| Judge (IVs), Premium-only | Free IV/EV view |
| Mystery gifts | Out of scope v1; event DB import possible later |
| Trading (GTS/Wonder/Room/Friend) | v2 trade server; v1 local trades that **trigger trade evolutions** |
| Silent hack blocks | Transparent PKHeX legality report, never blocking |
| Points/BP conversion | Out of scope (tied to live games) |
| Items can't be stored | Item vault (original feature) |

## Milestones

- [x] **M0** — Scaffold + PKHeX proof of life (solution, license, SaveFileService, smoke endpoint, fixture save)
- [x] **M1** — Vault core: EF Core schema, deposit/withdraw with PKH conversion, versioned backups, JSON API, 12 tests
- [x] **M2** — Web UI v1: save library, side-by-side box browser, drag-and-drop, Pokémon detail panel, sprite pipeline
- [ ] **M3** — Living dex + regional dexes; transparent legality reports; search/filter/sort; bulk ops; local trades with trade-evolution triggering; item vault; strict/free transfer toggle
- [ ] **M4** — Romhack support: profile-driven `ISaveFilePlugin` readers (JSON profiles: save size, offsets, species table, box count); pokeemerald-expansion default profile; community profiles droppable in `data/profiles/`
- [ ] **M5** — Pokémon Essentials: Ruby Marshal parser for `Game.rxdata`, `EssentialsSaveFile` mapping to PKM (PK3-neutral), relaxed legality rules for fangame origins, round-trip tests
- [ ] **M6** — Polish: Docker image, compose example, GitHub Actions CI, self-host docs, `docs/v2-trading.md` design
- [ ] **v2** (documented, not built in v1): federated self-hosted trade server, GTS-style listings with real filters (shiny/IV/ball/legitimacy/block-list)

## Risks / constraints

- GPL-3.0 everywhere (PKHeX.Core dependency).
- No live-console access: users supply dumps (Checkpoint/JKSM, emulator `.sav`/`.dsv`, flashcart dumps).
- Essentials forks vary; Marshal parser targets mainline Essentials v21, profile-extensible.
- Only openly-licensed sprite packs in repo; no ripped copyrighted assets committed.
