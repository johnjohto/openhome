// Hand-typed mirror of src/OpenHome.Core/Dtos.cs (System.Text.Json defaults:
// PascalCase C# members serialize as camelCase JSON).

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
