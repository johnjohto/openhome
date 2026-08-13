# Self-hosting OpenHOME with Docker

The repo ships a multi-stage `Dockerfile` that builds the web UI (`web/dist`),
publishes the ASP.NET Core server, and packs both into a slim
`mcr.microsoft.com/dotnet/aspnet:10.0` runtime image. The server serves the UI
itself — one container, one port.

## Requirements

- Docker (or any OCI runtime that can build a Dockerfile). For Compose: Docker Compose v2 (`docker compose`).

## Build and run

```bash
docker build -t openhome .
docker run -d --name openhome -p 8080:8080 -v openhome-data:/data openhome
```

Then open <http://localhost:8080>. First run needs no setup: the container
creates the data directories and the SQLite database on startup. Upload a save
file from the UI (Save Library page) and you're running.

### docker compose

```bash
docker compose up -d --build
```

The included `docker-compose.yml` is the same setup: builds the image, maps
port 8080, mounts the named volume `openhome-data` at `/data`.

## Data layout

All mutable state lives under the data root — `/data` inside the container
(set by `OPENHOME_DATA`, which the image defaults to `/data`). Mount a volume
there and nothing is lost when the container is replaced.

| Path | Contents |
| --- | --- |
| `/data/openhome.db` | SQLite database: save registry, vault boxes, stored Pokémon |
| `/data/saves/` | Registered copies of your save files |
| `/data/backups/{saveId}/{timestamp}.sav` | Snapshot taken before every save write/import |

(`data/profiles/` for romhack profiles is planned for a later milestone — see
`docs/architecture.md`.)

## Backup and restore

Everything that matters is in the data volume, so backup is a file copy.
Stop the app first so SQLite is not mid-write.

```bash
docker compose stop openhome   # or: docker stop openhome

# Back up a named volume to a tarball on the host:
docker run --rm -v openhome-data:/data -v "$PWD":/backup busybox \
  tar czf /backup/openhome-backup.tar.gz -C /data .

# Restore:
docker run --rm -v openhome-data:/data -v "$PWD":/backup busybox \
  sh -c "tar xzf /backup/openhome-backup.tar.gz -C /data"

docker compose start openhome
```

If you bind-mount a host directory instead of a named volume
(`-v /srv/openhome:/data`), backing up is just copying that folder.

## Sprites

Slot sprites (`/sprites/{species}.png`) are ripped game assets: copyrighted,
gitignored, and **not** shipped in the image. The UI renders a Poké Ball
placeholder for any slot without a sprite, so everything works without them.

To bake sprites into the image, fetch them on the host **before** building —
they land in `web/public/sprites/`, which `npm run build` copies into
`web/dist` and the web stage therefore picks up:

```bash
cd web && npm run fetch-sprites && cd ..   # all species 1–1025, normal + shiny
docker build -t openhome .
```

Fetching into the data volume at runtime is not supported — sprites are static
assets served from the web build, not data files.

## Configuration

| Environment variable | Default (image) | Purpose |
| --- | --- | --- |
| `OPENHOME_DATA` | `/data` | Data root (db, saves, backups). Outside Docker it defaults to `data/` next to the app. |
| `ASPNETCORE_URLS` | `http://+:8080` | Listen address. Change here if you remap ports. |

Notes:

- The container serves plain HTTP on 8080. Put a reverse proxy (Caddy, nginx,
  Traefik) in front for HTTPS if you expose it beyond localhost.
- There is no authentication — v1 is local-only by design. Do not expose the
  port on a network you don't trust.

## Updating

```bash
docker compose build && docker compose up -d
```

The data volume survives rebuilds; EF Core `EnsureCreated` keeps the database
intact across version bumps.

## CI and branch protection (repo maintainers)

`.github/workflows/ci.yml` runs on every push and pull request to `master`:
`dotnet build`, `dotnet test`, then `npm ci`, `npm test`, `npm run build` in
`web/`. Branch protection cannot be configured from a workflow file — set it
once in the GitHub UI:

1. Repo → **Settings** → **Branches** → **Add branch ruleset** (or classic branch protection rule).
2. Target branch: `master`.
3. Enable **Require status checks to pass before merging** and select the
   `build-and-test` check.
4. Optionally enable **Require a pull request before merging**.
