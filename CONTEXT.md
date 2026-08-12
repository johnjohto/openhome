# OpenHOME

A self-hosted Pokémon HOME alternative: you run it on your own hardware, it reads your own save
dumps, and every write is snapshotted before it happens. Built on PKHeX.Core.

The app has one narrating voice — Bill (`docs/voice-bill.md`). Terms below carry two forms: the
**canonical** name used in code, API and field labels, and the **in-voice** form used in any sentence
Bill speaks. Both are binding.

## Storage

**Vault**:
The user's own persistent storage, independent of any save file. Unlimited boxes, lives on the
user's disk.
_Avoid_: cloud, HOME, the service, the server
_In voice_: the vault, or **the station** for the running system as a whole

**Vault box**:
One named 30-slot grid inside the Vault. Created freely; there is no cap.
_Avoid_: folder, bank, storage unit

**Slot**:
One addressable position in a box, identified by box and index. Every action Bill reports names one.
_Avoid_: cell, space, spot

**Stored Pokémon**:
A Pokémon held in the Vault as a format-neutral record, with its origin preserved.
_Avoid_: entry, item, mon, record (when speaking — *record* is a data word, not a living one)
_In voice_: name it, or *the one from Emerald*. Never a count where a name will do.

**Deposit / Withdraw / Move**:
Save → Vault, Vault → Save, and Vault → Vault. Three distinct operations; never collapse them into
"transfer" in a label.
_Avoid_: import, export, sync

**Backup**:
A numbered snapshot of a save file taken before any write to it. Never overwritten; the history is
walkable.
_Avoid_: autosave, restore point, version history
_In voice_: always by number — *backup 7*, never "a backup"

## Worlds

**Origin world**:
The game a Pokémon or save came from. The canonical field is `game`, because that is the file's own
word for itself.
_Avoid_: title, version, ROM
_In voice_: **world**. Never "game", never "title"

**Hand-built world**:
A romhack or fangame — anything read through a profile rather than a stock PKHeX save format.
_Avoid_: fake, unofficial, modded, illegitimate
_In voice_: *a world somebody built by hand*. It is never ranked below a shipped one

**Profile**:
A JSON description of where a hand-built world keeps its boxes, species table and save offsets.
Droppable into `data/profiles/` by anyone.
_Avoid_: plugin, driver, adapter

**Crossing**:
Moving a Pokémon between worlds of different generations, which may not be reversible.
_Avoid_: transfer (ambiguous with deposit/withdraw), migration
_In voice_: *sending it home*, *folding it down* when the target format cannot hold it

**Legality report**:
PKHeX's assessment of whether a Pokémon could have arisen legitimately. Always shown, never enforced.
_Avoid_: validation, ban, block, cheat detection
_In voice_: *flagged, not refused*

**Strict mode / Free mode**:
Strict applies the official one-way locks; Free ignores them and warns instead. Free is the default.
_Avoid_: safe mode, legit mode, cheat mode

## The far side

**The glass**:
The boundary between the worlds Bill can read and the one the user is standing in. He knows it's
there; he has never been across it and will not speculate about it.
_Avoid_: the fourth wall, the screen, real life, the player's world
_In voice_: named **once**, on the About screen, and nowhere else in the app

**The fork**:
Bill's departure from the pokemon-one continuity — the reason there is a Bill here at all. Dev-facing
only. See `Pokemon2/bible/60-story/characters/bill.md`.
_Avoid_: any appearance in shipped UI text beyond the About screen
