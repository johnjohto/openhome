# PKHeX.Core integration notes

Everything verified against **PKHeX.Core 26.7.7** (net10.0). Update this file whenever the pinned version changes.

## Loading & saving

- `SaveUtil.GetSaveFile(path)` / `TryGetSaveFile(Memory<byte>, out SaveFile?, string? path)` — auto-detects all mainline formats (GB→Switch, Stadium/Colosseum/XD/Bank), handles emulator wrappers (DeSmuME `.dsv`, ARDS, NSO) via `SaveUtil.Handlers`.
- Persist with `sav.Write().ToArray()`.
- Box names: `((IBoxDetailName)sav).GetBoxName(i)` — the interface cast is required.
- Trainer name: `sav.OT` exists on `SaveFile`; on `PKM` it's renamed **`OriginalTrainerName`** (`PKM.OT` is gone).

## Items

- Item names: `GameInfo.Strings.itemlist` (a `string[]` field; index by national item id, bounds-check first).
- Held items: `pk.HeldItem` (int, 0 = none). Mutating the entity from `GetBoxSlotAtIndex` does NOT write through on formats that decrypt per slot (gen 5 at least); write back with `sav.SetBoxSlotAtIndex(pk, box, slot)` before persisting.

## Transfer checks (strict/free mode)

- Species presence in a game: `sav.Personal.IsPresentInGame(species, form)` (ints; e.g. Black has up to 649, BDSP lacks Victini 494 and Wyrdeer 899).
- The generation an entity currently lives in comes from `pkh.LatestGameData`'s type (`GameDataPB7` = 7, `GameDataPK8/PB8/PA8` = 8, `GameDataPK9/PA9` = 9). `pkh.Version` is `Any` and `pkh.Format` is 0 for vault-stored PKH, so neither helps here. `PKH.ConvertFromPKM` keeps a gen-9 `Version` only when the source entity has one set; `Version = Any` drops the gen-9 side data on `Rebuild()` and latest falls back to PK8.
- `EntityConverter.IsConvertibleToFormat(pk, gen)` answers "does a conversion route exist", NOT "is this transfer legal": a gen-9 PKH converts into PK8/PB8 fine (PKHeX grafts the core data). The no-route case still fails: PKH to PK5 returns `NoTransferRoute`.
- `EntityConverter.ConvertToType` on a PKH that was never `Rebuild()`-serialized throws "Unrecognized format: 0" via an internal clone, so always reparse from rebuilt bytes first.

## Boxes & slots

- `GetBoxSlot(offset)` is **protected**. Public API: `GetBoxSlotAtIndex(box, slot)` / `(index)` and `SetBoxSlotAtIndex(pk, box, slot)`.
- Clear a slot: `sav.SetBoxSlotAtIndex(sav.BlankPKM, box, slot)`.
- `sav.BlankPKM` reports `CurrentLevel=1` — detect empty slots with `Species == 0`, not level.
- `DecryptedBoxData`/`DecryptedPartyData` don't exist; use `pk.Data` (Span) or `WriteDecryptedDataStored`.

## HOME container (PKH) — the vault format

- `PKH.ConvertFromPKM(pk)` converts any entity; `ConvertToPK8()/ConvertToPB8()/ConvertToPA8()/ConvertToPK9()/ConvertToPA9()/ConvertToPB7()` go back. `EntityConverter.ConvertToType(pkh, targetType, out _)` is the generic fallback; `EntityConverter.IsConvertibleToFormat(pkh, gen)` is a cheap pre-check.
- **Current moves live in per-game side data** (`PKH.Move1..4` read `LatestGameData`), and `PKH.CopyFrom` only creates that side for `PB7/PK7/PK8/PB8/PA8/PK9/PA9` sources — a gen ≤5 entity converted straight to PKH **silently loses its moves** (IVs/EVs/core data survive). `VaultService` upgrades gen ≤5 entities to PK8 via `EntityConverter.ConvertToType(pk, typeof(PK8))` before `ConvertFromPKM`; the transfer re-localizes un-nicknamed species names, so the original nickname is restored afterwards.
- **Serialize with `pkh.Rebuild()`** — `ConvertFromPKM` leaves `DataVersion=0`/size fields unset, and the `PKH(Memory<byte>)` ctor throws "Unrecognized format: 0" on raw `Data`.
- `ConvertFromPKM` does **not** assign a HOME tracker (stays 0) — VaultService mints a random unique nonzero ulong at deposit.
- Gen ≤5 string terminator leak: PK5→PKH conversion can leave `0xFFFF` in `Nickname`/`OriginalTrainerName`. Stored metadata/DTOs are sanitized; raw bytes still carry it (revisit in M3).

## Legality (for M3)

- `new LegalityAnalysis(pk)` → `.Valid`, `.Parsed`, `.Results` (per-check `CheckResult`), `.EncounterMatch`.
- Custom rules: `ExternalLegalityCheck.ExternalCheckers`.
- Custom save formats: `SaveUtil.CustomSaveReaders` (`ISaveFilePlugin`) — the M4/M5 hook.

## Trade evolution

- `EvolutionTree.GetEvolutionTree(sav.Context)` (public static; `SaveFile.Context` is the save's `EntityContext`) → cast to `IEvolutionNetwork`, then `.Forward.GetForward(species, form)` returns `ReadOnlyMemory<EvolutionMethod>` for that species' forward evolutions.
- `EvolutionMethod`: `Species`, `Form`, `Argument`, `Method` (`EvolutionType`), `GetDestinationForm(currentForm)`. Trade triggers are `EvolutionType.Trade`, `TradeHeldItem` (requires `pk.HeldItem == Argument`, e.g. Metal Coat = 233), and `TradeShelmetKarrablast` (Karrablast 588 ↔ Shelmet 616 only evolve when traded for each other; `Argument` is 0).
- Gate the result with `sav.Personal.IsPresentInGame(species, form)` — only evolve when the destination game actually contains the evolved species. `IEvolutionNetwork.Forward`/`Reverse` are explicit interface implementations, so the cast is required.
- Applying an evolution is just `pk.Species`/`pk.Form` assignment + `pk.RefreshChecksum()`; `TradeService` does this on the receiving save after the swap.
- `EvolutionType.IsTrade` exists as a C# 14 extension *property* (`EvolutionTypeExtensions.get_IsTrade`) — don't call it as a method.

## Pokédex data

- Species names + national ceiling: `GameInfo.Strings.specieslist` — index 0 is the `"---"` placeholder, so the max valid species id is `Count() - 1` (1025 at 26.7.7). `SaveFile.MaxSpeciesID` is the save's own range (e.g. 649 for Black).
- Cross-version per-save dex: `sav.HasPokeDex`, `sav.GetSeen(ushort)` / `sav.GetCaught(ushort)`, plus `SeenCount`/`CaughtCount` rollups. Used by `DexService`; saves without a dex fall back to box contents.
- **Blank-save quirk**: public `SetSeen`/`SetCaught` are no-ops on a never-initialized `BlankSaveFile` — only the non-public `SetDex(PKM)` populates the dex block (tests reach it via reflection). Placing a Pokémon with `SetBoxSlotAtIndex` (default `EntityImportSettings`) also marks it seen+caught automatically.

## Test fixtures — BlankSaveFile quirks

`var sav = BlankSaveFile.Get(GameVersion.B, "TEST"); sav.State.Edited = true; sav.Write().ToArray();`

- Only **B, B2, BD** reliably round-trip through `SaveUtil` detection.
- Other gens: blank saves write a superset of blocks (size detection rejects them), or `Write()` throws `ArgumentOutOfRangeException` (SAV3/SAV4 `WriteSectors`). Known upstream quirk — blank saves are meant for in-app use, not disk round-trips.
- Committed fixture: `tests/fixtures/saves/blank-bw.sav` (blank Pokémon Black).

## NuGet / licensing

- Only `PKHeX.Core` is on NuGet (zero deps). Drawing/sprite assemblies are source-only.
- GPLv3 — this project is GPL-3.0 accordingly.
- Consumers worth watching for API-churn patterns: SysBot.NET, pkNX, PKHeX.Web, PKMDS-Blazor.
