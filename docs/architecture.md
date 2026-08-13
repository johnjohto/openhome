# Architecture

## Stack

- **Backend**: ASP.NET Core 10 minimal APIs, C#, `PKHeX.Core` 26.7.7 (pinned), EF Core + SQLite
- **Frontend (M2)**: React + Vite + TypeScript + Tailwind, dnd-kit drag-and-drop, TanStack Query
- **Tests**: xunit (backend), Vitest (frontend)

## Layout

```
openhome/
├── OpenHome.slnx
├── src/
│   ├── OpenHome.Core/        # Domain + PKHeX facade + persistence (DbContext)
│   │   ├── Persistence/      # OpenHomeDbContext, entities
│   │   ├── SaveFileService   # SaveUtil facade → SaveSummary
│   │   ├── SaveLibraryService# register/copy/hash save files
│   │   ├── VaultService      # deposit/withdraw/move, box listing
│   │   ├── TradeService      # local trades between saves + trade evolution on receipt
│   │   ├── BackupService     # pre-write snapshots to data/backups/
│   │   └── Dtos.cs           # JSON DTOs — no PKHeX types cross the wire
│   ├── OpenHome.Formats/     # (M4/M5) ISaveFilePlugin readers: hack profiles, Essentials
│   └── OpenHome.Server/      # Minimal API, DI, static hosting of web build
├── web/                      # (M2) React+Vite app
├── tests/                    # OpenHome.Core.Tests, OpenHome.Formats.Tests, fixtures/saves/
└── data/                     # gitignored runtime: openhome.db, saves/, backups/, profiles/
```

## Data model (M1, implemented)

- `SaveFileRecord`: Id, FilePath, Sha256, Game, TrainerName, RegisteredAt, LastOpenedAt
- `VaultBox`: Id, Name, Order — 30 slots per box; "Vault 1" auto-created, new box auto-created when full
- `StoredPokemon`: Id, VaultBoxId+Slot (unique), Data (serialized `PKH` bytes via `Rebuild()`), denormalized Species/Form/IsShiny/Level/Nickname/OTName/OriginGame/HomeTracker/DepositedAt

## Core flows

- **Deposit**: load save → `GetBoxSlotAtIndex(box, slot)` → `PKH.ConvertFromPKM` → mint unique nonzero HomeTracker → `pkh.Rebuild()` bytes stored → slot cleared in save → save persisted (backup snapshot first)
- **Withdraw**: stored PKH → target format: dedicated `ConvertToPK8/PB8/PA8/PK9/PA9/PB7` when the save's `BlankPKM` matches, else `EntityConverter.ConvertToType`; no route → `UnsupportedConversionException` (HTTP 422). This reproduces HOME's no-backwards-transfers semantics by default.
- **Backups**: every save write (and import) snapshots the previous file to `data/backups/{saveId}/{timestamp}.sav`

## Design rules

1. All PKHeX types stay behind OpenHome.Core services; API and web UI only see DTOs.
2. Every save mutation snapshots first — backups are cheap, saves are irreplaceable.
3. PKHeX.Core version is pinned; bump intentionally and record notes in `docs/pkhex-integration.md`.
4. Test fixtures are generated (`BlankSaveFile.Get`) or committed synthetic saves — never copyrighted data.
