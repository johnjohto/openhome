# Bill is the only voice in the app

Every user-facing string in OpenHOME is written as one character — Bill, the man who built the
storage system — filling the role Grand Oak fills in Pokémon HOME. The rules and the authored line
set live in [`docs/voice-bill.md`](../voice-bill.md); the generator is the voice card at
`character-voice/voices/bill-openhome.md`. This is a product decision, not decoration: the
character's first rule (*nothing lands without an address* — box, slot, backup number) encodes the
project's actual differentiator, which is that a self-hosted vault can be checked and a cloud one
cannot.

## Considered Options

- **Neutral UI copy.** Cheaper, no voice discipline needed on contributions, and it makes OpenHOME
  indistinguishable in tone from every other save editor. Rejected: the thing being replaced has a
  friendly guide, and matching its warmth while beating its honesty is most of the pitch.
- **Grand Oak's actual register — a reassuring guide who says it's handled.** Rejected on the same
  grounds we reject the hostage mechanic: "it's taken care of" is the sentence a self-hosted tool
  exists to stop needing.
- **Canon Bill, imitated directly.** Rejected: the source card is in-copyright and marked
  `blend-only`. What ships is a user-owned continuity forked from the pokemon-one Bill, which is the
  project owner's own character.

## Consequences

- **Contributions are reviewable against a written rule**, not taste. A string that reports a result
  without naming where it landed fails review. So does one containing "we", or any of the banned
  fiction vocabulary listed in `docs/voice-bill.md`.
- **Field labels and error payloads are in scope.** The API's own error strings surface in the UI
  banner (`"Box 0 slot 0 is empty — nothing to deposit."`), so server-side messages are voice
  surface too, not just React copy.
- **One feature is owed.** The "world he hasn't seen before" beat requires a persisted seen-set keyed
  by origin game; until it exists, registration uses the second-sighting line. Tracked for M3.
- **The character carries a cross-project dependency.** His origin is a fork committed in
  `Pokemon2/bible/60-story/characters/bill.md` and registered in pokemon-one's `sequel-hooks.md`.
  Nothing in OpenHOME breaks if pokemon-two never ships that arc — the origin is never stated in the
  UI beyond the About screen — but the three projects are now telling one story and should stay
  consistent.
- **Makers are never named in shipped text** (no Nintendo, no Game Freak). This is the voice rule and
  the legal posture at the same time.
