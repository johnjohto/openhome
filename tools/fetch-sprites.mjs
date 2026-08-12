#!/usr/bin/env node
/**
 * fetch-sprites.mjs — download species sprites into web/public/sprites/.
 *
 * Sprites come from the PokeAPI sprites repository (game assets, non-free) —
 * they are gitignored and must be fetched per machine, never committed:
 *
 *   node tools/fetch-sprites.mjs            # all species, normal + shiny
 *   node tools/fetch-sprites.mjs 1-151      # a range
 *   node tools/fetch-sprites.mjs --no-shiny # skip shiny variants
 *
 * Output: web/public/sprites/{species}.png and {species}-shiny.png, served by
 * the app at /sprites/... (in dev via the Vite proxy/static dir, in prod from
 * the built bundle). Missing sprites fall back to an inline SVG in the UI.
 */
import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const REPO_RAW = 'https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon';
const outDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../web/public/sprites');

const args = process.argv.slice(2);
const shiny = !args.includes('--no-shiny');
const rangeArg = args.find((a) => /^\d+(-\d+)?$/.test(a));
const [from, to] = rangeArg ? rangeArg.split('-').map(Number).map((n, i, a) => a[i] ?? a[0]) : [1, 1025];

await mkdir(outDir, { recursive: true });

let ok = 0;
let failed = 0;
for (let species = from; species <= to; species++) {
  const targets = [`${species}.png`];
  if (shiny) targets.push(`${species}-shiny.png`);
  for (const file of targets) {
    const url = `${REPO_RAW}/${file.startsWith(`${species}-`) ? 'shiny/' : ''}${file.replace(`${species}-`, '')}`;
    try {
      const res = await fetch(url);
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      await writeFile(path.join(outDir, file), Buffer.from(await res.arrayBuffer()));
      ok++;
    } catch (err) {
      failed++;
      console.warn(`  ! ${file}: ${err.message}`);
    }
  }
  if (species % 100 === 0) console.log(`  … ${species}/${to}`);
}

console.log(`Done: ${ok} sprites written to ${outDir}${failed ? ` (${failed} failed)` : ''}.`);
