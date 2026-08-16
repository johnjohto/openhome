# OpenHOME — agent guide

Open-source, self-hosted Pokémon HOME alternative built on PKHeX.Core. GPL-3.0.

## Build & test

- `dotnet build` / `dotnet test` from the repo root (.NET 10)
- Frontend: `npm run dev` / `npm test` / `npm run build` in `web/` (Vite dev server proxies `/api` → `http://localhost:5140`; run the server with the `http` launch profile). Sprites are gitignored — `npm run fetch-sprites` downloads them locally.
- The server serves `web/dist` (with SPA fallback) when it exists; M2 smoke steps live in `docs/smoke-test.md`.
- Docker: root `Dockerfile` (web build + server publish + runtime, data at `/data`), `docker-compose.yml`, self-host guide in `docs/self-hosting.md`. CI: `.github/workflows/ci.yml` runs dotnet build/test and web test/build on push/PR.

## Conventions

- PKHeX.Core is **pinned** (26.7.7) — never bump casually; monthly upstream releases break APIs. Verified API facts live in `docs/pkhex-integration.md`; update it when the version changes.
- No PKHeX types cross the API wire — OpenHome.Core services expose DTOs only (`Dtos.cs`).
- Every save-file write snapshots the previous file via `BackupService` first. Never bypass it.
- Test fixtures are generated (`BlankSaveFile.Get`) or synthetic committed saves — never copyrighted data. See the BlankSaveFile quirks in `docs/pkhex-integration.md`.
- Design docs: `docs/plan.md` (milestones), `docs/architecture.md`, `docs/v2-trading.md`.
- All user-facing copy — React strings **and** API error messages, which surface in the UI banner —
  is written in one character voice: Bill. Rules and the authored line set are in
  `docs/voice-bill.md`; the decision is `docs/adr/0001-bill-is-the-only-voice.md`. Never write a UI
  string that reports a result without saying where it landed, and never write "we".
- `CONTEXT.md` is the glossary: every domain term carries a canonical form (code, API, labels) and an
  in-voice form (anything Bill says). Both are binding.

## Agent skills

### Issue tracker

Issues are tracked as GitHub issues on `johnjohto/openhome` via the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Domain docs

Single-context: `CONTEXT.md` + `docs/adr/` at the repo root (created lazily by `/domain-modeling`). See `docs/agents/domain.md`.
