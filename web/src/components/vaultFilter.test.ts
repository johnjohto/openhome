import { describe, expect, it } from 'vitest';
import { EMPTY_VAULT_FILTERS, isFilterActive, matchesVaultFilters, type VaultFilters } from './vaultFilter';
import type { BoxSlotSummary, StoredPokemonSummary } from '../api/types';

function slot(overrides: Partial<BoxSlotSummary>): BoxSlotSummary {
  return {
    box: 0,
    slot: 0,
    isEmpty: false,
    species: 25,
    form: 0,
    nickname: 'Pika',
    level: 12,
    isShiny: false,
    storedPokemonId: 'p1',
    legalityValid: true,
    ...overrides,
  };
}

function summary(overrides: Partial<StoredPokemonSummary>): StoredPokemonSummary {
  return {
    id: 'p1',
    boxId: 'b1',
    boxName: 'Vault 1',
    slot: 0,
    species: 25,
    form: 0,
    isShiny: false,
    level: 12,
    nickname: 'Pika',
    otName: 'TEST',
    originGame: 'Black',
    homeTracker: 1,
    depositedAt: '2026-08-01T10:00:00Z',
    ...overrides,
  };
}

function filters(overrides: Partial<VaultFilters>): VaultFilters {
  return { ...EMPTY_VAULT_FILTERS, ...overrides };
}

describe('isFilterActive', () => {
  it('is false for the empty filter set and true once anything is set', () => {
    expect(isFilterActive(EMPTY_VAULT_FILTERS)).toBe(false);
    expect(isFilterActive(filters({ text: 'pik' }))).toBe(true);
    expect(isFilterActive(filters({ text: '  ' }))).toBe(false);
    expect(isFilterActive(filters({ shiny: 'shiny' }))).toBe(true);
    expect(isFilterActive(filters({ legality: 'invalid' }))).toBe(true);
    expect(isFilterActive(filters({ originGame: 'Black' }))).toBe(true);
  });
});

describe('matchesVaultFilters', () => {
  it('always passes empty slots', () => {
    const empty = slot({ isEmpty: true, storedPokemonId: null, legalityValid: null });
    expect(matchesVaultFilters(empty, undefined, filters({ text: 'zzz', shiny: 'shiny' }))).toBe(true);
  });

  it('matches text against nickname and OT case-insensitively', () => {
    expect(matchesVaultFilters(slot({}), summary({}), filters({ text: 'PIK' }))).toBe(true);
    expect(matchesVaultFilters(slot({}), summary({}), filters({ text: 'est' }))).toBe(true);
    expect(matchesVaultFilters(slot({}), summary({}), filters({ text: 'zzz' }))).toBe(false);
  });

  it('matches pure numbers against the species id', () => {
    expect(matchesVaultFilters(slot({ species: 25 }), summary({}), filters({ text: '25' }))).toBe(true);
    expect(matchesVaultFilters(slot({ species: 25 }), summary({}), filters({ text: '133' }))).toBe(false);
  });

  it('filters by shiny in both directions', () => {
    const shinySlot = slot({ isShiny: true });
    expect(matchesVaultFilters(shinySlot, summary({}), filters({ shiny: 'shiny' }))).toBe(true);
    expect(matchesVaultFilters(shinySlot, summary({}), filters({ shiny: 'normal' }))).toBe(false);
    expect(matchesVaultFilters(slot({}), summary({}), filters({ shiny: 'shiny' }))).toBe(false);
  });

  it('treats a null legality verdict as neither valid nor invalid', () => {
    const unknown = slot({ legalityValid: null });
    expect(matchesVaultFilters(unknown, summary({}), filters({ legality: 'valid' }))).toBe(false);
    expect(matchesVaultFilters(unknown, summary({}), filters({ legality: 'invalid' }))).toBe(false);
    expect(matchesVaultFilters(slot({ legalityValid: false }), summary({}), filters({ legality: 'invalid' }))).toBe(true);
  });

  it('filters by origin game via the summary row', () => {
    expect(matchesVaultFilters(slot({}), summary({ originGame: 'Black' }), filters({ originGame: 'Black' }))).toBe(true);
    expect(matchesVaultFilters(slot({}), summary({ originGame: 'Black' }), filters({ originGame: 'White' }))).toBe(false);
    expect(matchesVaultFilters(slot({}), undefined, filters({ originGame: 'Black' }))).toBe(false);
  });

  it('AND-combines all active filters', () => {
    const s = slot({ isShiny: true });
    const ok = matchesVaultFilters(s, summary({}), filters({ text: 'pik', shiny: 'shiny', legality: 'valid' }));
    const fail = matchesVaultFilters(s, summary({}), filters({ text: 'pik', shiny: 'normal' }));
    expect(ok).toBe(true);
    expect(fail).toBe(false);
  });
});
