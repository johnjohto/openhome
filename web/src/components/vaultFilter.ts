import type { BoxSlotSummary, StoredPokemonSummary } from '../api/types';

/**
 * Client-side vault grid filter state. The box browser filters in place over data
 * it already has (GET /api/vault/boxes + GET /api/vault/pokemon) — at vault scale
 * a server round-trip per keystroke buys nothing; the server-side query endpoint
 * (GET /api/vault/pokemon/query) exists for API consumers and larger vaults.
 */
export interface VaultFilters {
  /** Case-insensitive substring over nickname and OT; a pure number also matches the species id. */
  text: string;
  shiny: 'any' | 'shiny' | 'normal';
  legality: 'any' | 'valid' | 'invalid';
  /** "any" or an exact origin game name. */
  originGame: string;
}

export const EMPTY_VAULT_FILTERS: VaultFilters = {
  text: '',
  shiny: 'any',
  legality: 'any',
  originGame: 'any',
};

export function isFilterActive(filters: VaultFilters): boolean {
  return (
    filters.text.trim() !== '' ||
    filters.shiny !== 'any' ||
    filters.legality !== 'any' ||
    filters.originGame !== 'any'
  );
}

/**
 * Whether an occupied slot passes the filters. Empty slots always pass (they are
 * never dimmed). `summary` is the stored-Pokémon index row carrying OT and origin
 * game — fields the slot summary does not denormalize; a missing summary fails
 * active OT/origin filters.
 */
export function matchesVaultFilters(
  slot: BoxSlotSummary,
  summary: StoredPokemonSummary | undefined,
  filters: VaultFilters,
): boolean {
  if (slot.isEmpty) return true;

  const text = filters.text.trim().toLowerCase();
  if (text !== '') {
    const numeric = /^\d+$/.test(text);
    const matchesText =
      slot.nickname.toLowerCase().includes(text) ||
      (summary?.otName.toLowerCase().includes(text) ?? false) ||
      (numeric && slot.species === Number(text));
    if (!matchesText) return false;
  }

  if (filters.shiny === 'shiny' && !slot.isShiny) return false;
  if (filters.shiny === 'normal' && slot.isShiny) return false;

  // A null verdict (analysis unavailable) matches neither "valid" nor "invalid",
  // mirroring the server-side query endpoint.
  if (filters.legality === 'valid' && slot.legalityValid !== true) return false;
  if (filters.legality === 'invalid' && slot.legalityValid !== false) return false;

  if (filters.originGame !== 'any' && summary?.originGame !== filters.originGame) return false;

  return true;
}
