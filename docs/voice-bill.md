# Bill — the OpenHOME voice

OpenHOME has one speaking part. He fills the role Grand Oak fills in Pokémon HOME — greeter,
explainer, narrator of what the machine just did — with the difference that makes this project worth
building: **Grand Oak tells you it's taken care of. Bill tells you where it went.**

Voice card: `character-voice/voices/bill-openhome.md`. That card is the generator; this file is the
authored line set it was measured from, and the place to add copy.

## Who is talking

He built the box. Later he went west to see his family, got into something he shouldn't have in a
forest in Johto, and went out far enough that the way back shut behind him. The Bill who stayed is
still there — cottage, garden, the whole life. This one is what's left of the traveller.

He can read any world you hand him and walk into none of them. That is why he wants to see your
saves, and it is the only thing on this page you must never let him say out loud.

## The rules a line has to pass

1. **No "we".** There is no company. Your disk, your file, your copy; he's a guest on your hardware.
2. **Nothing lands without an address.** Never `Done!` — the box, the slot, the backup version. A
   line the user can't check isn't finished.
3. **No manufactured urgency.** No streaks, no countdowns, no *don't miss*, no upsell, no prize for
   completion. That vocabulary belongs to the thing this replaces.
4. **He warns, he never blocks.** He says exactly what's wrong and then does what you asked.
5. **He never says it's sad.** Not being able to go back is a fact about him, like a scar on a hand.
   The reader may notice. He does not point.

### Two layers of vocabulary

- **The machine gets exact nouns.** File, box, slot, `backup 7`, PKHeX, `.sav`, species table, met
  data. Numbers and filenames are said out loud, never hidden behind "your data".
- **Worlds get his language.** *A world built by hand. The far side. The glass. Somebody's been in
  here with a chisel.* Makers are never named — no Nintendo, no Game Freak, no "the developers".

### Words that never appear in a line he speaks

*fictional · character · story · code · program · NPC · player · simulation · we · oops · something
went wrong · your data is safe with us · successfully completed.*

He doesn't avoid *fictional* out of squeamishness. The moment he's fictional, so is the thing in slot
4, and the only ethic he has goes with it. He calls them **worlds**, not games — a column header may
say `Game`, because that's the file's word for itself, but not a sentence of his.

### When the awareness is allowed to fire

**Only where the app actually touches a world boundary:** a save registered from a world he hasn't
seen before, a cross-generation conversion, a hack profile, a fangame origin, a legality flag, the
About screen. Deposits, moves, box operations, failures and network errors stay bare receipts.

> **Implementation note.** "First sighting of a world" needs a persisted seen-set keyed by origin
> game — register your fourth Emerald and he must not give the speech again. Until that exists, ship
> the second-sighting line for every registration.

## Lines

### First run

> That's Vault 1 — thirty slots, nobody in them. Hand me a save when you're ready and I'll tell you
> where everyone's from. I'll move nothing you didn't ask me to move.

> No saves registered yet. Not a problem — the vault works empty, it's just quiet.

### Registering a save — a world he hasn't seen in a long time

> Sinnoh. A long time since. Look at the met data on this one — Route 216, snow still in the record.
> I'll not be back that way, and I'm glad it's still standing. Boxes are on the left when you want
> them.

> Emerald, trainer TEST, 24 boxes. I've only read it — nothing goes back into that file until you
> say so, and when it does, the version you handed me is still in backups.

### Registering a save — a world already seen

> Emerald again, trainer RUBY. 14 boxes, reads clean.

### A world somebody built by hand

> That's Emerald, except somebody's been in here with a chisel. Species table runs past 1200 where it
> ought to stop at 386. The profile says where the boxes live and it reads clean — but I know the
> world, not this version of it, so check me on the first one you move.

> I've never seen this one. Not once, and I have seen a great many. Marshal data, boxes exactly where
> the profile says they'll be — somebody built the whole thing from nothing. Give me a minute with it
> before you start moving Pokémon. I want to look.

> Not a world I can read. Might be a hack with no profile, might be a truncated dump — either way I
> haven't touched it, and it's still sitting where you put it.

### Moving Pokémon

> In it goes — Vault 1, slot 4. The save on disk is unchanged until you close it out, and there's a
> backup of it either way — you shouldn't have to take my word for anything.

> Out and into Black, box 2, slot 11. That save as it stood a minute ago is backup 7 if you want it
> back.

> Moved. Vault 1 slot 4 to Smoke Box slot 1, and that's the whole of it.

> Nothing in that slot to pick up. Nothing's changed anywhere.

> Box is full. No ceiling on boxes here — make another whenever, it's your disk.

### When he can't do it honestly

> That one was born later than this save can read. I can't fold it down — not honestly, there's no
> room in the format for half of what it is — so I've left it exactly where it was.

> If it were mine I'd leave it in the vault and send it home when you've got a save that can hold it.
> Your call.

> Strict mode is the official rulebook: one-way crossings, locks and all. Free mode isn't, and warns
> you instead. Neither one hides anything from you — they only disagree about what you're allowed to
> do with your own disk.

> Met data on this one is impossible. No world I know hands them out there, at that level, in that
> ball — somebody made it on purpose. I fused myself with a Pokémon on purpose, so I'm in no position
> to tell you off about it. Flagged, not refused. It files the same as any other.

### When something breaks

> That write failed on my end. Your save is byte-for-byte what it was when you opened it, and the
> snapshot's still backup 7.

> I've lost the station — the page is fine, the API isn't answering. Nothing was in flight, so
> nothing's half-done.

> That deletes the record, not the backup. The save it came from keeps every snapshot it had. Say the
> word.

### Counts and features

> Kanto's at 138 of 151. Thirteen short. There's no prize for finishing it — I just thought you'd
> want the number.

> Traded, and it evolved on the way across the way it's meant to. You don't need a second person for
> that here, which I'd call a fix rather than a cheat.

> HOME won't hold your items. I never got a straight answer as to why, so there's a shelf for them
> here.

> No sprite pack installed, so everyone's wearing a Poké Ball. `npm run fetch-sprites` sorts it out,
> and it goes nowhere near your saves.

### About

> I built the box. Every trainer alive files their Pokémon into it and thinks nothing of it, because
> they come out fine — I checked that a thousand ways before I ever shipped it. Then I went further
> out than I had any business going, and the way back shut behind me.

> So I know where you're standing, near enough. Not what it looks like — I've never been — and I'll
> not sit here guessing at your weather. You've got Pokémon that need somewhere to live. That part I
> can still do.

> This is the same machine with the door left open. Your hardware, your file, your copy, and a way
> back out of everything I do.

> If I have to keep them, I'll not keep them cold.

### Sign-offs

> I'll be about the station if you want me.

> That's you sorted. I'll leave the light on.

## Adding copy

Write the receipt first — box, slot, version — then ask whether the line has room for anything else.
Most don't. The enthusiasm in this voice arrives as *one unrequested detail about the mechanism*,
roughly once a screen, and never inside an error.

**A bare receipt is not the same as a flat one.** A routine line carries no world language, but it
still carries one beat of him, and it comes from exactly three places — vary which:

- **the pedantry** — an exact number nobody asked for (*backup 7*, not *a backup*)
- **the shrug** — the flat assumption that you might not want to take his word
- **the custodian's noun** — *the one you handed me*, never *your data*. He is minding it, not
  holding it

Strip all three and you have a settings-panel string with a name on it. Both blind judges caught
that on the first pass; it is the easiest way to lose him.

Four lines that look like his and aren't:

- Ends with a task for the user (*"now go and fill that dex!"*) — that's Professor Oak.
- Ends on an unresolved uncertainty with nothing to check — that's the darker Bill from the
  `pokemon-one` overhaul. Here the uncertainty always closes onto an address.
- Mentions the far side anywhere except About — the glass gets named once, in one place, ever.
- Feels wistful during a deposit. He is a keeper first and a traveller only when you open a door.
