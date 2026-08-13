import type { DexSpeciesProgress, NationalDexProgress } from '../api/types';

/**
 * View-model mapping for the dex page grid: turns the server's per-species
 * progress rows into display cells (zero-padded dex number, owned/shiny state,
 * distinct owned form count) and computes completion percentages.
 */

export interface DexCell {
  species: number;
  /** Zero-padded national dex number, e.g. "0025". */
  number: string;
  name: string;
  owned: boolean;
  shinyOwned: boolean;
  /** Distinct owned forms (a plain owned species with no form variants is 1). */
  formCount: number;
}

export function buildDexCells(progress: NationalDexProgress): DexCell[] {
  return progress.species.map(toCell);
}

function toCell(s: DexSpeciesProgress): DexCell {
  return {
    species: s.species,
    number: String(s.species).padStart(4, '0'),
    name: s.name,
    owned: s.owned,
    shinyOwned: s.shinyOwned,
    formCount: s.ownedForms.length,
  };
}

/** Completion percentage (0-100, one decimal) — 0 when the dex is empty. */
export function completionPercent(owned: number, total: number): number {
  if (total <= 0) return 0;
  return Math.round((owned / total) * 1000) / 10;
}
