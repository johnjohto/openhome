# PKHeX.Core integration notes

Everything verified against **PKHeX.Core 26.7.7** (net10.0). Update this file whenever the pinned version changes.

## Loading & saving

- `SaveUtil.GetSaveFile(path)` / `TryGetSaveFile(Memory<byte>, out SaveFile?, string? path)` — auto-detects all mainline formats (GB→Switch, Stadium/Colosseum/XD/Bank), handles emulator wrappers (DeSmuME `.dsv`, ARDS, NSO) via `SaveUtil.Handlers`.
- Persist with `sav.Write().ToArray()`.
- Box names: `((IBoxDetailName)sav).GetBoxName(i)` — the interface cast is required.
- Trainer name: `sav.OT` exists on `SaveFile`; on `PKM` it's renamed **`OriginalTrainerName`** (`PKM.OT` is gone).

## Boxes & slots

- `GetBoxSlot(offset)` is **protected**. Public API: `GetBoxSlotAtIndex(box, slot)` / `(index)` and `SetBoxSlotAtIndex(pk, box, slot)`.
- Clear a slot: `sav.SetBoxSlotAtIndex(sav.BlankPKM, box, slot)`.
- `sav.BlankPKM` reports `CurrentLevel=1` — detect empty slots with `Species == 0`, not level.
- `DecryptedBoxData`/`DecryptedPartyData` don't exist; use `pk.Data` (Span) or `WriteDecryptedDataStored`.

## HOME container (PKH) — the vault format

- `PKH.ConvertFromPKM(pk)` converts any entity; `ConvertToPK8()/ConvertToPB8()/ConvertToPA8()/ConvertToPK9()/ConvertToPA9()/ConvertToPB7()` go back. `EntityConverter.ConvertToType(pkh, targetType, out _)` is the generic fallback; `EntityConverter.IsConvertibleToFormat(pkh, gen)` is a cheap pre-check.
- **Serialize with `pkh.Rebuild()`** — `ConvertFromPKM` leaves `DataVersion=0`/size fields unset, and the `PKH(Memory<byte>)` ctor throws "Unrecognized format: 0" on raw `Data`.
- `ConvertFromPKM` does **not** assign a HOME tracker (stays 0) — VaultService mints a random unique nonzero ulong at deposit.
- Gen ≤5 string terminator leak: PK5→PKH conversion can leave `0xFFFF` in `Nickname`/`OriginalTrainerName`. Stored metadata/DTOs are sanitized; raw bytes still carry it (revisit in M3).

## Legality (for M3)

- `new LegalityAnalysis(pk)` → `.Valid`, `.Parsed`, `.Results` (per-check `CheckResult`), `.EncounterMatch`.
- Custom rules: `ExternalLegalityCheck.ExternalCheckers`.
- Custom save formats: `SaveUtil.CustomSaveReaders` (`ISaveFilePlugin`) — the M4/M5 hook.

## Test fixtures — BlankSaveFile quirks

`var sav = BlankSaveFile.Get(GameVersion.B, "TEST"); sav.State.Edited = true; sav.Write().ToArray();`

- Only **B, B2, BD** reliably round-trip through `SaveUtil` detection.
- Other gens: blank saves write a superset of blocks (size detection rejects them), or `Write()` throws `ArgumentOutOfRangeException` (SAV3/SAV4 `WriteSectors`). Known upstream quirk — blank saves are meant for in-app use, not disk round-trips.
- Committed fixture: `tests/fixtures/saves/blank-bw.sav` (blank Pokémon Black).

## NuGet / licensing

- Only `PKHeX.Core` is on NuGet (zero deps). Drawing/sprite assemblies are source-only.
- GPLv3 — this project is GPL-3.0 accordingly.
- Consumers worth watching for API-churn patterns: SysBot.NET, pkNX, PKHeX.Web, PKMDS-Blazor.
