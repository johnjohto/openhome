import { describe, expect, it } from 'vitest';
import type { NationalDexProgress } from '../api/types';
import { buildDexCells, completionPercent } from './dexGrid';

function progress(species: NationalDexProgress['species']): NationalDexProgress {
  return { total: species.length, owned: species.filter((s) => s.owned).length, shinyOwned: species.filter((s) => s.shinyOwned).length, species };
}

describe('buildDexCells', () => {
  it('maps species progress rows to grid cells with padded numbers and form counts', () => {
    const cells = buildDexCells(
      progress([
        { species: 1, name: 'Bulbasaur', owned: false, shinyOwned: false, ownedForms: [] },
        { species: 25, name: 'Pikachu', owned: true, shinyOwned: true, ownedForms: [0] },
        { species: 201, name: 'Unown', owned: true, shinyOwned: false, ownedForms: [1, 2, 5] },
      ]),
    );

    expect(cells).toEqual([
      { species: 1, number: '0001', name: 'Bulbasaur', owned: false, shinyOwned: false, formCount: 0 },
      { species: 25, number: '0025', name: 'Pikachu', owned: true, shinyOwned: true, formCount: 1 },
      { species: 201, number: '0201', name: 'Unown', owned: true, shinyOwned: false, formCount: 3 },
    ]);
  });
});

describe('completionPercent', () => {
  it('computes a one-decimal percentage', () => {
    expect(completionPercent(1, 3)).toBe(33.3);
    expect(completionPercent(649, 649)).toBe(100);
    expect(completionPercent(0, 1025)).toBe(0);
  });

  it('returns 0 for an empty dex instead of dividing by zero', () => {
    expect(completionPercent(0, 0)).toBe(0);
  });
});
