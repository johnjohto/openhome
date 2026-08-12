# M2 smoke test — Web UI v1

Verified 2026-08-12 with a scratch data dir (`OPENHOME_DATA` pointed at a temp folder).
Steps to reproduce manually:

1. Build the frontend once (so the server can also serve it):
   ```bash
   cd web && npm install && npm run build
   ```
2. Start the API (http profile, port 5140):
   ```bash
   dotnet run --project src/OpenHome.Server --launch-profile http
   ```
3. Start the dev server in another shell:
   ```bash
   cd web && npm run dev   # http://localhost:5173, proxies /api → 5140
   ```

## What was verified

- `GET /api/vault/boxes` — auto-creates "Vault 1" with a 30-slot grid ✔
- `POST /api/saves` with `tests/fixtures/saves/blank-bw.sav` → registers "Black / TEST" ✔
- `GET /api/saves/{id}/boxes` → 24 boxes × 30 slots, all empty (blank fixture) ✔
- `POST /api/vault/deposit` on an empty slot → `400 {"error":"Box 0 slot 0 is empty — nothing to deposit."}`
  (this is the error shape the UI surfaces in its alert banner) ✔
- `POST /api/vault/boxes {"name":"Smoke Box"}` → box created ✔
- Server serves `web/dist` at `/` and the SPA fallback returns `index.html` (200 text/html)
  for unknown non-API routes ✔
- Through the Vite proxy (`http://localhost:5173`): `/api/saves`, `/api/vault/boxes` and `/` all respond ✔

## 2026-08-12 rerun — ticket #2 vault index/detail endpoints

Same scratch setup, plus a fabricated Black save with a nicknamed, moved Pikachu in box 0
slot 0 (`BlankSaveFile.Get(GameVersion.B, "TEST")` + `State.Edited = true`, written to disk):

- `GET /api/vault/pokemon` before deposit → `[]` ✔
- `POST /api/saves` + `POST /api/vault/deposit` (0, 0) → 200 with the denormalized summary ✔
- `GET /api/vault/pokemon` → one entry: species 25, nickname "Pika", OT "TEST", origin game
  "Black", nonzero HOME tracker, box name "Vault 1", slot 0 ✔
- `GET /api/vault/pokemon/{id}` → same metadata plus `ivs`/`evs` (camelCase — the C# record
  pins `JsonPropertyName`, otherwise the acronyms serialize as `iVs`/`eVs`) and four moves
  with IDs and names from PKHeX's string list (85 "Thunderbolt", 129 "Swift", 0 "(None)" ×2) ✔
- `GET /api/vault/pokemon/{unknown-guid}` → `404 {"error":"No stored Pokémon with id …."}` ✔
- Note for repeat runs: a stale `bin/` will deposit without the gen ≤5 move-preserving upgrade —
  rebuild before smoking (`dotnet run --no-build` does not).

## Not covered by the smoke run

- Deposit/withdraw/move happy paths — the committed fixture save has empty boxes. These are
  covered by the backend xunit suite (`dotnet test`, 12 tests) and can be exercised manually
  with any non-blank save dump via the UI: drag save slot → vault (deposit), vault → empty
  save slot (withdraw), vault → vault (move).
- Real sprites: `web/public/sprites/` is gitignored; without `npm run fetch-sprites` every
  slot renders the inline SVG Poké Ball placeholder.
