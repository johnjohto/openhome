# OpenHOME

An open-source, self-hosted alternative to Pokémon HOME — backup your saves, store unlimited Pokémon, transfer between games, and track your living dex. Built on [PKHeX.Core](https://github.com/kwsch/PKHeX).

## Why

Pokémon HOME's biggest pain points, fixed:

- **No paywall, no hostage Pokémon** — unlimited boxes on your own disk.
- **Real save backups** — versioned snapshots of entire save files, not just the Pokémon (Pokémon games are excluded from NSO cloud saves).
- **Transparent legality** — full PKHeX legality report per Pokémon, shown to you, never silently blocking.
- **One app, every feature** — browser UI works from desktop and phone; no mobile/Switch feature split.
- **Free transfers** — optional strict mode mimics official transfer rules; default mode lets you move anything, with warnings.
- **Romhacks & fangames** — profile-driven support for pokeemerald-expansion-style hacks and Pokémon Essentials saves (phased).

## Feature status

See the milestone plan in `docs/`. v1 is local-only; a self-hosted trade server (GTS-style with real filters) is designed for but lands in v2.

## Requirements

- .NET 10 SDK
- Node.js 20+

## Getting started

```bash
dotnet build
dotnet run --project src/OpenHome.Server
```

Save files: supply your own dumps (Checkpoint/JKSM for 3DS/Switch, emulator `.sav`/`.dsv`, flashcart dumps). OpenHOME never touches a live console.

## Run with Docker

```bash
docker build -t openhome .
docker run -d -p 8080:8080 -v openhome-data:/data openhome
```

or `docker compose up -d --build`. The image serves the UI and API on port 8080
and keeps all state in the `/data` volume. See `docs/self-hosting.md` for first
run, data layout, backup/restore, and sprites.

## CI

`.github/workflows/ci.yml` builds and tests backend and web on every push/PR to
`master`. Maintainers: enable branch protection for `master` in repo Settings
(require the `build-and-test` check) — it's a one-time manual step, see
`docs/self-hosting.md#ci-and-branch-protection-repo-maintainers`.

## License

GPL-3.0 — required by the PKHeX.Core dependency. See `LICENSE`.
