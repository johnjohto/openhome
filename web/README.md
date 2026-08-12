# OpenHOME web UI

React + Vite + TypeScript frontend for OpenHOME (M2). Dark-theme, HOME-style box interface:
save library, side-by-side save/vault box browser with drag-and-drop, Pokémon detail panel.

## Stack

- Vite 7 + React 19 + TypeScript, Tailwind CSS v4 (`@tailwindcss/vite`)
- TanStack Query for server state; dnd-kit for drag-and-drop
- Vitest + Testing Library

## Commands

```bash
npm install
npm run dev        # Vite dev server, proxies /api → http://localhost:5140
npm test           # Vitest
npm run build      # tsc + vite build → web/dist
npm run preview    # serve the production build
```

Run `dotnet run --project src/OpenHome.Server --launch-profile http` (from the repo root)
first — the dev proxy targets `http://localhost:5140` (the server's `http` launch profile).

## Sprites

Slot sprites load from `/sprites/{species}.png` (`{species}-shiny.png` for shinies). Sprites
are ripped game assets and are **gitignored** (`web/public/sprites/`); fetch them locally:

```bash
npm run fetch-sprites          # all species 1–1025, normal + shiny
node ../tools/fetch-sprites.mjs 1-151 --no-shiny
```

Any slot without a sprite file renders an inline SVG Poké Ball placeholder, so the UI is
fully usable without running the fetch.

## Layout

- `src/api/` — hand-typed DTO mirror (`types.ts`), fetch client (`client.ts`), TanStack Query hooks (`hooks.ts`)
- `src/components/` — `BoxGrid` (6×5 grid), `BoxSlotCell` (drag source + drop target), `BoxSwitcher`, `PokemonDetail`, `PokemonSprite`
- `src/pages/` — `SaveLibraryPage` (upload + list), `BoxBrowserPage` (side-by-side boxes + dnd)
- `src/test/` — Vitest setup and unit tests

## Drag-and-drop semantics

- save slot → vault slot: `POST /api/vault/deposit` (server picks the first free vault slot)
- vault slot → empty save slot: `POST /api/vault/withdraw`
- vault slot → empty vault slot: `POST /api/vault/move`
- save → save: not an OpenHOME operation, ignored

After every mutation the `saves` and `vault` query caches are invalidated.
