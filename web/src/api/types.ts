// Hand-typed mirror of src/OpenHome.Core/Dtos.cs (System.Text.Json defaults:
// PascalCase C# members serialize as camelCase JSON).

/** An item by national id plus its English display name. */
export interface ItemInfo {
  id: number;
  name: string;
}

/** One slot in a save-file or vault box grid. */
export interface BoxSlotSummary {
  box: number;
  slot: number;
  isEmpty: boolean;
  species: number;
  form: number;
  nickname: string;
  level: number;
  isShiny: boolean;
  /** Set only for vault slots (the StoredPokemon id); null for save slots. */
  storedPokemonId: string | null;
  /** PKHeX legality verdict for occupied vault slots; null when unknown/unavailable. */
  legalityValid: boolean | null;
  /** Held item for occupied save slots; null for vault slots (the detail endpoint reports it). */
  heldItem: ItemInfo | null;
}

/** A named box of a registered save file. */
export interface BoxView {
  box: number;
  name: string;
  slots: BoxSlotSummary[];
}

/** A vault box with its slot grid. */
export interface VaultBoxView {
  id: string;
  name: string;
  order: number;
  slots: BoxSlotSummary[];
}

/** Metadata of a Pokémon stored in the vault (mutation responses). */
export interface StoredPokemonSummary {
  id: string;
  boxId: string;
  boxName: string;
  slot: number;
  species: number;
  form: number;
  isShiny: boolean;
  level: number;
  nickname: string;
  otName: string;
  originGame: string;
  /** uint64 — the API emits it as a JSON number. */
  homeTracker: number;
  depositedAt: string;
}

/** A save file registered in the library. */
export interface RegisteredSaveSummary {
  id: string;
  fileName: string;
  game: string;
  trainerName: string;
  sha256: string;
  registeredAt: string;
  lastOpenedAt: string;
}

/** Six battle stats, in canonical order. */
export interface StatSet {
  hp: number;
  attack: number;
  defense: number;
  spAttack: number;
  spDefense: number;
  speed: number;
}

/** A learned move: national move ID plus its display name. */
export interface MoveInfo {
  id: number;
  name: string;
}

/** Full detail of a stored Pokémon: summary metadata plus IVs, EVs and moves. */
export interface StoredPokemonDetail extends StoredPokemonSummary {
  ivs: StatSet;
  evs: StatSet;
  moves: MoveInfo[];
  heldItem: ItemInfo | null;
}

/**
 * Result of a vault withdraw: the Pokémon as it left the vault, plus the
 * transfer-legality warnings raised before the move (free mode only — strict
 * mode refuses instead with HTTP 422).
 */
export interface WithdrawResult {
  pokemon: StoredPokemonSummary;
  warnings: string[];
}

/** A stack of identical items in the item vault. */
export interface VaultItemSummary {
  itemId: number;
  name: string;
  count: number;
}

/** Runtime configuration the UI renders: the transfer mode. */
export interface ServerConfig {
  strictTransfers: boolean;
}

/** One row of GET /api/vault/pokemon/query: summary metadata plus the legality verdict. */
export interface StoredPokemonQueryResult extends StoredPokemonSummary {
  /** PKHeX legality verdict; null when analysis was unavailable. */
  legalityValid: boolean | null;
}

/**
 * Query parameters for GET /api/vault/pokemon/query. All filters are optional and
 * AND-combined; `search` matches nickname/OT substrings (case-insensitive);
 * `sortBy` names a denormalized column (species, form, level, nickname, ot,
 * origingame, tracker, depositedat, box).
 */
export interface VaultQueryParams {
  species?: number;
  minLevel?: number;
  maxLevel?: number;
  shiny?: boolean;
  originGame?: string;
  legality?: 'valid' | 'invalid';
  search?: string;
  sortBy?: string;
  sortDesc?: boolean;
}

/** A (box, slot) coordinate in a save file's box storage. */
export interface BoxSlotRef {
  box: number;
  slot: number;
}

/** One line of a PKHeX legality report. */
export interface LegalityCheckItem {
  identifier: string;
  /** "Valid" | "Fishy" | "Invalid" */
  severity: string;
  valid: boolean;
  message: string;
}

/** Full legality report for a stored Pokémon. Informational only — never enforced. */
export interface LegalityReport {
  valid: boolean;
  parsed: boolean;
  checks: LegalityCheckItem[];
}

/** National-dex progress for one species: owned, shiny-owned, and owned forms. */
export interface DexSpeciesProgress {
  species: number;
  name: string;
  owned: boolean;
  shinyOwned: boolean;
  ownedForms: number[];
}

/** Living national dex computed from current vault contents (one entry per species id). */
export interface NationalDexProgress {
  total: number;
  owned: number;
  shinyOwned: number;
  species: DexSpeciesProgress[];
}

/**
 * Dex progress of one registered save. When `usesSaveDexData` is false the save
 * format has no Pokédex and the numbers are species present in its boxes.
 */
export interface SaveDexProgress {
  saveId: string;
  game: string;
  trainerName: string;
  usesSaveDexData: boolean;
  total: number;
  seen: number;
  caught: number;
  seenSpecies: number[];
  caughtSpecies: number[];
}

export interface CreateBoxRequest {
  name?: string | null;
}

export interface DepositRequest {
  saveId: string;
  box: number;
  slot: number;
}

export interface WithdrawRequest {
  pokemonId: string;
  saveId: string;
  box: number;
  slot: number;
}

export interface MoveRequest {
  pokemonId: string;
  boxId: string;
  slot: number;
}

export interface BulkDepositRequest {
  saveId: string;
  slots: BoxSlotRef[];
}

export interface BulkMoveRequest {
  pokemonIds: string[];
  boxId: string;
}

export interface ReleaseRequest {
  pokemonIds: string[];
}

/** One side of a completed trade: what this save received, and whether it evolved. */
export interface TradeSlotResult {
  saveId: string;
  box: number;
  slot: number;
  species: number;
  form: number;
  nickname: string;
  level: number;
  isShiny: boolean;
  speciesName: string;
  evolved: boolean;
  evolvedFromSpecies: number;
  evolvedFromName: string | null;
}

/** Report of a completed trade: sideA is what save A received, sideB what B received. */
export interface TradeReport {
  sideA: TradeSlotResult;
  sideB: TradeSlotResult;
}

export interface TradeRequest {
  saveAId: string;
  boxA: number;
  slotA: number;
  saveBId: string;
  boxB: number;
  slotB: number;
}

export interface ItemDepositRequest {
  saveId: string;
  box: number;
  slot: number;
}

export interface ItemWithdrawRequest {
  itemId: number;
  saveId: string;
  box: number;
  slot: number;
}
