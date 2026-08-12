# OpenHOME — agent guide

Open-source, self-hosted Pokémon HOME alternative built on PKHeX.Core. GPL-3.0.

## Build & test

- `dotnet build` / `dotnet test` from the repo root (.NET 10)
- Frontend (from M2): `npm run dev` / `npm test` in `web/`

## Conventions

- PKHeX.Core is **pinned** (26.7.7) — never bump casually; monthly upstream releases break APIs. Verified API facts live in `docs/pkhex-integration.md`; update it when the version changes.
- No PKHeX types cross the API wire — OpenHome.Core services expose DTOs only (`Dtos.cs`).
- Every save-file write snapshots the previous file via `BackupService` first. Never bypass it.
- Test fixtures are generated (`BlankSaveFile.Get`) or synthetic committed saves — never copyrighted data. See the BlankSaveFile quirks in `docs/pkhex-integration.md`.
- Design docs: `docs/plan.md` (milestones), `docs/architecture.md`, `docs/v2-trading.md`.

## Agent skills

### Issue tracker

Issues are tracked as GitHub issues on `johnjohto/openhome` via the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Domain docs

Single-context: `CONTEXT.md` + `docs/adr/` at the repo root (created lazily by `/domain-modeling`). See `docs/agents/domain.md`.
