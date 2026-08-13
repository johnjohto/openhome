import type {
  BoxView,
  BulkDepositRequest,
  BulkMoveRequest,
  CreateBoxRequest,
  DepositRequest,
  LegalityReport,
  MoveRequest,
  RegisteredSaveSummary,
  ReleaseRequest,
  StoredPokemonDetail,
  StoredPokemonQueryResult,
  StoredPokemonSummary,
  VaultBoxView,
  VaultQueryParams,
  WithdrawRequest,
} from './types';

/** Thrown for any non-2xx API response; `status` is the HTTP status code. */
export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

async function parseError(res: Response): Promise<ApiError> {
  let message = `Request failed with status ${res.status}`;
  try {
    const body: unknown = await res.json();
    if (body && typeof body === 'object' && 'error' in body && typeof body.error === 'string') {
      message = body.error;
    }
  } catch {
    // Body wasn't JSON — keep the generic message.
  }
  return new ApiError(res.status, message);
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(path, init);
  if (!res.ok) throw await parseError(res);
  return (await res.json()) as T;
}

function post<T>(path: string, body?: unknown): Promise<T> {
  return request<T>(path, {
    method: 'POST',
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
}

/** GET /api/saves */
export function listSaves(): Promise<RegisteredSaveSummary[]> {
  return request('/api/saves');
}

/** POST /api/saves — multipart upload, registers the save in the library. */
export function uploadSave(file: File): Promise<RegisteredSaveSummary> {
  const form = new FormData();
  form.append('file', file);
  return request('/api/saves', { method: 'POST', body: form });
}

/** GET /api/saves/{id}/boxes */
export function getSaveBoxes(saveId: string): Promise<BoxView[]> {
  return request(`/api/saves/${saveId}/boxes`);
}

/** GET /api/vault/boxes */
export function listVaultBoxes(): Promise<VaultBoxView[]> {
  return request('/api/vault/boxes');
}

/** GET /api/vault/pokemon — every stored Pokémon with denormalized metadata. */
export function listVaultPokemon(): Promise<StoredPokemonSummary[]> {
  return request('/api/vault/pokemon');
}

/** GET /api/vault/pokemon/query — server-side filter/sort over the denormalized columns. */
export function queryVaultPokemon(params: VaultQueryParams): Promise<StoredPokemonQueryResult[]> {
  const qs = new URLSearchParams();
  if (params.species !== undefined) qs.set('species', String(params.species));
  if (params.minLevel !== undefined) qs.set('minLevel', String(params.minLevel));
  if (params.maxLevel !== undefined) qs.set('maxLevel', String(params.maxLevel));
  if (params.shiny !== undefined) qs.set('shiny', String(params.shiny));
  if (params.originGame) qs.set('originGame', params.originGame);
  if (params.legality) qs.set('legality', params.legality);
  if (params.search) qs.set('search', params.search);
  if (params.sortBy) qs.set('sortBy', params.sortBy);
  if (params.sortDesc) qs.set('sortDesc', 'true');
  const query = qs.toString();
  return request(`/api/vault/pokemon/query${query ? `?${query}` : ''}`);
}

/** GET /api/vault/pokemon/{id} — one stored Pokémon with IVs, EVs and moves. */
export function getVaultPokemon(id: string): Promise<StoredPokemonDetail> {
  return request(`/api/vault/pokemon/${id}`);
}

/** GET /api/vault/pokemon/{id}/legality — the full PKHeX legality report. */
export function getVaultLegality(id: string): Promise<LegalityReport> {
  return request(`/api/vault/pokemon/${id}/legality`);
}

/** POST /api/vault/boxes */
export function createVaultBox(req?: CreateBoxRequest): Promise<VaultBoxView> {
  return post('/api/vault/boxes', req ?? null);
}

/** POST /api/vault/deposit — save slot → first free vault slot. */
export function deposit(req: DepositRequest): Promise<StoredPokemonSummary> {
  return post('/api/vault/deposit', req);
}

/** POST /api/vault/withdraw — vault slot → save slot. */
export function withdraw(req: WithdrawRequest): Promise<StoredPokemonSummary> {
  return post('/api/vault/withdraw', req);
}

/** POST /api/vault/move — vault slot → vault slot. */
export function move(req: MoveRequest): Promise<StoredPokemonSummary> {
  return post('/api/vault/move', req);
}

/** POST /api/vault/deposit/bulk — several save slots → free vault slots, in order. */
export function depositMany(req: BulkDepositRequest): Promise<StoredPokemonSummary[]> {
  return post('/api/vault/deposit/bulk', req);
}

/** POST /api/vault/move/bulk — several stored Pokémon → free slots of a target box, in order. */
export function moveMany(req: BulkMoveRequest): Promise<StoredPokemonSummary[]> {
  return post('/api/vault/move/bulk', req);
}

/** POST /api/vault/release — permanent; the response reports what was released. */
export function release(req: ReleaseRequest): Promise<StoredPokemonSummary[]> {
  return post('/api/vault/release', req);
}
