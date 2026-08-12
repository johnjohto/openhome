# Domain Docs

How the engineering skills should consume this repo's domain documentation when exploring the codebase.

## Before exploring, read these

- **`CONTEXT.md`** at the repo root
- **`docs/adr/`** — read ADRs that touch the area you're about to work in
- **`docs/plan.md`**, **`docs/architecture.md`**, **`docs/pkhex-integration.md`** — this project's own design docs (milestone plan, architecture, PKHeX API notes)

If `CONTEXT.md` or `docs/adr/` don't exist, **proceed silently**. The `/domain-modeling` skill creates them lazily when terms or decisions actually get resolved.

## Layout

Single-context repo:

```
/
├── CONTEXT.md
├── docs/adr/
│   └── 0001-....md
└── src/
```

## Use the glossary's vocabulary

When your output names a domain concept (in an issue title, a refactor proposal, a hypothesis, a test name), use the term as defined in `CONTEXT.md`. Don't drift to synonyms the glossary explicitly avoids.

Established domain terms for this repo (until `CONTEXT.md` exists): **vault** (the unlimited box storage), **save library** (registered save files), **deposit/withdraw** (save ↔ vault moves), **backup** (versioned save snapshot), **stored Pokémon** (a `PKH` blob in the vault).

## Flag ADR conflicts

If your output contradicts an existing ADR, surface it explicitly rather than silently overriding:

> _Contradicts ADR-0007 (…) — but worth reopening because…_
