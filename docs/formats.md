# OpenHome.Formats: romhack profiles and Essentials saves

This document covers the two custom save formats added in M4/M5 (tickets #10 and #11),
how they plug into PKHeX, and their known limits. Read `docs/pkhex-integration.md` first
for the general PKHeX rules.

## Registration

`FormatsRegistration.RegisterAll(profilesDirectory)` is the single hook the server calls
once at startup, before any save is loaded. It appends readers to
`SaveUtil.CustomSaveReaders`: one `ProfileSaveReader` per romhack profile first, then the
`EssentialsSaveReader` last (it recognizes by content, so it must not shadow profiles).
The call is idempotent; `Reset()` exists for tests. After the call,
`FormatsRegistration.RegisteredProfiles` lists the loaded profile names and
`ProfileErrors` lists any profile files that failed to parse.

Custom readers run before PKHeX's built-in detection, so both readers are deliberately
conservative about what they claim; anything they decline falls through to the normal
readers.

## Ticket #10: GBA romhack profiles

### What a profile describes

Generation III GBA saves (Ruby/Sapphire/Emerald/FRLG and their decomp romhacks) are 128 KB
of flash: 32 sectors of 4 KB, two rotating copies ("slots") of 14 sectors, then four
sectors of extras (Hall of Fame and friends). Each sector carries 3968 bytes of data and a
12 byte footer (sector id, checksum, signature 0x08012025, save counter). Within a slot,
sector id 0 holds the trainer block (SaveBlock2), ids 1-4 the large block (SaveBlock1,
party at 0x238), and ids 5-13 the PC storage (current box byte, then 14 boxes of 30
80-byte encrypted BoxPokemon records, then box names and wallpapers).

pokeemerald-expansion keeps all of that. What changes is the content: species are stored
in national dex order instead of the vanilla gen-3 internal order, the species/move/item
tables are larger, and the blocks can grow or shift when builders flip the FREE_* config
switches. A profile captures all of this in JSON so a new hack is a file drop, not a
recompile. The bundled default is `profiles/pokeemerald-expansion.json` (also embedded as
a resource, so the server works before the user copies anything into `data/profiles/`).
Files in the profiles folder with the same `name` override the bundled one.

### Schema (all integers also accept "0x.." hex strings)

- `name`, `description`, `family` ("gba-gen3")
- `saveSize` (131072), `version` (PKHeX game code, "E"), `language` (2 = English)
- `sectorSize` (4096), `sectorDataSize` (3968), `mainSectorCount` (14), `saveSlotCount` (2)
- `footer`: `idOffset` (4084), `checksumOffset` (4086), `signatureOffset` (4088),
  `counterOffset` (4092), `signature` (0x08012025)
- `trainerBlock` / `partyBlock` / `storageBlock`: `sectorStart`, `sectorCount`, `size`.
  `size` is the block's struct size and drives checksum coverage: each sector's checksum
  is the folded u32 sum over `min(remaining block bytes, 3968)`, exactly as the game
  computes it.
- `trainer`: `nameOffset` (0), `nameMaxLength` (7), `nameStride` (8), `genderOffset` (8),
  `idOffset` (10), playtime offsets (14/16/17)
- `party`: `countOffset` (564), `offset` (568), `count` (6), all within the party block
- `boxes`: `currentBoxOffset` (0), `dataOffset` (4), `boxCount` (14), `slotsPerBox` (30),
  `boxNameOffset` (33604), `boxNameMaxLength` (8), `boxNameStride` (9),
  `wallpaperOffset` (33730), all within the storage block
- `pokemon`: `speciesOrder` ("national" or "gen3Internal") and the max-id fields
  (`maxSpeciesId`, `maxMoveId`, `maxItemId`, `maxBallId`, `maxAbilityId`)
- `detection`: `claimIfNationalSpecies`, `claimAllGen3`, `contentRules`
  (absolute offset + expected hex bytes)

### Detection

A vanilla Emerald save and an expansion save have the same size and the same sector
structure, so size alone cannot route them (and a wrong claim would hijack vanilla saves,
since custom readers run first). `ProfileSaveReader` claims a file only when:

1. the size matches and at least one slot has a complete, signed set of sector ids, and
2. either every `contentRules` probe matches, or an occupied party/box slot decrypts to a
   valid gen-3 checksum with a raw species value that is impossible in vanilla internal
   order (the 252-276 and 387-411 gaps, or anything above 412, the egg slot). The
   expansion stores national numbers, so any gen-3-or-later species produces exactly such
   a value.

`claimAllGen3` exists for hacks whose saves can be ambiguous (an expansion save holding
only gen 1-2 Pokemon is byte-compatible in its species values); it is off in the default
profile because it also claims genuine vanilla saves.

### Species re-mapping

Entities surface as PK3. With a national-order profile, the raw species is re-mapped on
read (`Species` setter routes national to internal and back) and written back as a
national number on deposit clear or withdraw, so the game round-trips. This is exact for
national dex 1-386. Species above 386 cannot be represented in the gen-3 entity format at
all; they read as species 0 and are left alone on write. Closing that gap needs a reader
that bridges slots to a newer entity format, and is deliberately out of scope here.

### Withdraw semantics

Deposited entities from a gen-3 save are upgraded to PK8 on their way into the vault
(the existing VaultService behavior). PKHeX has no PKH-to-PK3 transfer route, so
withdrawing into a profile save raises `UnsupportedConversionException` (HTTP 422), the
same HOME-parity rule the official formats follow. Relaxed fangame rules are ticket #12.

## Ticket #11: Pokémon Essentials (Game.rxdata)

### The Marshal subset

An Essentials v21 save is a single Ruby Marshal 4.8 stream holding one Hash with symbol
keys (`:player`, `:storage_system`, `:switches`, `:essentials_version`, and so on).
`RubyMarshalReader`/`RubyMarshalWriter` cover the subset Essentials uses:

- nil, true, false
- fixnums: inline small form, the 1-4 byte long form, and the bignum ('l') form Ruby uses
  beyond 31 bits
- floats (including nan/inf), strings with the 'I' instance-variable wrapper (the UTF-8
  encoding marker), symbols with symbol links, arrays, hashes with and without default
  values, plain objects (class name + ivar bag), user-defined ('u') and marshal_dump ('U')
  values, and object links

Structs, class/module references, regexps and extension wrappers are rejected with a clear
error; Essentials never emits them for this data. The writer emits symbols inline and never
uses links, which Ruby accepts; it is also the fixture builder for the test suite, and its
output for scalars is pinned byte-for-byte against real `Marshal.dump` output in the tests.

The whole tree is kept in memory. Unknown game state (game systems, switches, fangame
classes) round-trips untouched because only the party and box arrays are rewritten on save.

### Mapping Pokemon to PK8

`Pokemon` objects are ivar bags: `@species` is a symbol (`:PIKACHU`), `@moves` an array of
`Pokemon::Move` (`@id` symbol, `@pp`, `@ppup`), `@iv`/`@ev` hashes keyed by stat symbols
(:HP, :ATTACK, :DEFENSE, :SPEED, :SPECIALATTACK, :SPECIALDEFENSE), `@owner` a
`Pokemon::Owner` (`@id` packs TID16/SID16 low/high, `@name`, `@gender`, `@language`),
plus `@name` (nickname), `@form`, `@level`, `@exp`, `@nature`, `@item`, `@poke_ball`,
`@personalID`, `@shiny`, `@gender`.

`EssentialsMapper` converts these to PK8, the vault's neutral representation: it covers
every species and move Essentials v21 can contain and feeds the PKH deposit path without
the gen-5-and-older move loss. Essentials constants are the English display name in upper
case with punctuation removed, so species, moves, items, balls, natures and abilities are
resolved by normalizing both sides against PKHeX's English string tables; a two-entry
override table covers the Nidorans. Unresolvable symbols degrade to zero/defaults rather
than failing the load. The shiny flag is preserved by adjusting the PID's high word so the
gen-6+ shiny rule agrees with it (xor 0 shiny, 16 not). Essentials gender (true = male)
maps to PK8's numeric gender; nil (runtime-derived) maps to genderless, a cosmetic
approximation.

### The save file

`EssentialsSaveFile` is a PKHeX `SaveFile` backed by PK8 buffers: 6 party slots of 344
bytes and N boxes of 30 slots of 328 bytes, built from the Marshal tree at load. All the
standard plumbing (box listing, deposit clear, withdraw write) works on the buffers. On
write, the tree is re-synchronized: untouched slots keep their original Pokemon objects
(including any fangame-specific ivars), cleared slots become nil, and new or changed slots
are rebuilt from the PK8 with sensible defaults. `EssentialsSaveReader` claims a file only
when it starts with the Marshal 4.8 magic and parses to a hash containing a player with a
party or a PokemonStorage.

### Limits

- Pre-v19 Essentials saves (a sequence of Marshal values instead of one Hash) are not
  supported; the reader declines them.
- Legality: deposits from Essentials saves report through the normal transparent legality
  pipeline and will usually flag as invalid (fangame origins have no official encounter
  data). Nothing is blocked; relaxed fangame legality is ticket #12.
- Nicknames and OT names are capped at PK8's 12 characters, and held-mail, ribbons and
  contest stats have no PK8 home and are only preserved when the slot is never rewritten.
