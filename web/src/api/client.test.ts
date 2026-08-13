import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError, deposit, getSaveBoxes, getVaultLegality, getVaultPokemon, listSaves, listVaultBoxes, listVaultPokemon, move, uploadSave, withdraw } from './client';
import type { BoxView, LegalityReport, RegisteredSaveSummary, StoredPokemonDetail, StoredPokemonSummary, VaultBoxView } from './types';

const fetchMock = vi.fn();
vi.stubGlobal('fetch', fetchMock);

afterEach(() => {
  fetchMock.mockReset();
});

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

describe('API client', () => {
  it('maps GET /api/saves response to RegisteredSaveSummary', async () => {
    const payload: RegisteredSaveSummary[] = [
      {
        id: '3f4b9c1e-0000-4000-8000-000000000001',
        fileName: 'emerald.sav',
        game: 'Emerald',
        trainerName: 'MAY',
        sha256: 'ab12',
        registeredAt: '2026-08-01T10:00:00Z',
        lastOpenedAt: '2026-08-01T10:00:00Z',
      },
    ];
    fetchMock.mockResolvedValueOnce(jsonResponse(payload));

    const saves = await listSaves();

    expect(fetchMock).toHaveBeenCalledWith('/api/saves', undefined);
    expect(saves).toEqual(payload);
    expect(saves[0].game).toBe('Emerald');
  });

  it('maps GET /api/vault/boxes response to VaultBoxView with slots', async () => {
    const payload: VaultBoxView[] = [
      {
        id: '3f4b9c1e-0000-4000-8000-0000000000aa',
        name: 'Vault 1',
        order: 0,
        slots: [
          {
            box: 0,
            slot: 0,
            isEmpty: false,
            species: 25,
            form: 0,
            nickname: 'PIKACHU',
            level: 42,
            isShiny: true,
            storedPokemonId: '3f4b9c1e-0000-4000-8000-0000000000bb',
            legalityValid: true,
          },
        ],
      },
    ];
    fetchMock.mockResolvedValueOnce(jsonResponse(payload));

    const boxes = await listVaultBoxes();

    expect(boxes[0].slots[0].storedPokemonId).toBe('3f4b9c1e-0000-4000-8000-0000000000bb');
    expect(boxes[0].slots[0].isShiny).toBe(true);
  });

  it('requests save boxes by id', async () => {
    const payload: BoxView[] = [{ box: 0, name: 'Box 1', slots: [] }];
    fetchMock.mockResolvedValueOnce(jsonResponse(payload));

    const boxes = await getSaveBoxes('save-id-1');

    expect(fetchMock).toHaveBeenCalledWith('/api/saves/save-id-1/boxes', undefined);
    expect(boxes[0].name).toBe('Box 1');
  });

  it('posts deposit/withdraw/move with JSON bodies matching the server records', async () => {
    fetchMock.mockImplementation(() => Promise.resolve(jsonResponse({ id: 'x' })));

    await deposit({ saveId: 's1', box: 2, slot: 7 });
    expect(fetchMock).toHaveBeenLastCalledWith('/api/vault/deposit', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ saveId: 's1', box: 2, slot: 7 }),
    });

    await withdraw({ pokemonId: 'p1', saveId: 's1', box: 0, slot: 3 });
    expect(fetchMock).toHaveBeenLastCalledWith('/api/vault/withdraw', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ pokemonId: 'p1', saveId: 's1', box: 0, slot: 3 }),
    });

    await move({ pokemonId: 'p1', boxId: 'b1', slot: 29 });
    expect(fetchMock).toHaveBeenLastCalledWith('/api/vault/move', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ pokemonId: 'p1', boxId: 'b1', slot: 29 }),
    });
  });

  it('uploads a save as multipart form data under the "file" field', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ id: 'new-save' }));
    const file = new File(['bytes'], 'ruby.sav', { type: 'application/octet-stream' });

    await uploadSave(file);

    const [path, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(path).toBe('/api/saves');
    expect(init.method).toBe('POST');
    const form = init.body as FormData;
    expect(form.get('file')).toBeInstanceOf(File);
    expect((form.get('file') as File).name).toBe('ruby.sav');
  });

  it('maps GET /api/vault/pokemon to the stored Pokémon index', async () => {
    const payload: StoredPokemonSummary[] = [
      {
        id: '3f4b9c1e-0000-4000-8000-000000000001',
        boxId: '3f4b9c1e-0000-4000-8000-0000000000aa',
        boxName: 'Vault 1',
        slot: 0,
        species: 25,
        form: 0,
        isShiny: false,
        level: 42,
        nickname: 'Pika',
        otName: 'TEST',
        originGame: 'Black',
        homeTracker: 123456789,
        depositedAt: '2026-08-01T10:00:00Z',
      },
    ];
    fetchMock.mockResolvedValueOnce(jsonResponse(payload));

    const list = await listVaultPokemon();

    expect(fetchMock).toHaveBeenCalledWith('/api/vault/pokemon', undefined);
    expect(list[0].otName).toBe('TEST');
    expect(list[0].homeTracker).toBe(123456789);
  });

  it('maps GET /api/vault/pokemon/{id} to a StoredPokemonDetail with IVs, EVs and moves', async () => {
    const payload: StoredPokemonDetail = {
      id: '3f4b9c1e-0000-4000-8000-000000000001',
      boxId: '3f4b9c1e-0000-4000-8000-0000000000aa',
      boxName: 'Vault 1',
      slot: 0,
      species: 25,
      form: 0,
      isShiny: false,
      level: 42,
      nickname: 'Pika',
      otName: 'TEST',
      originGame: 'Black',
      homeTracker: 123456789,
      depositedAt: '2026-08-01T10:00:00Z',
      ivs: { hp: 31, attack: 30, defense: 29, spAttack: 28, spDefense: 27, speed: 26 },
      evs: { hp: 4, attack: 252, defense: 0, spAttack: 0, spDefense: 0, speed: 252 },
      moves: [
        { id: 85, name: 'Thunderbolt' },
        { id: 129, name: 'Swift' },
        { id: 98, name: 'Quick Attack' },
        { id: 0, name: '(None)' },
      ],
    };
    fetchMock.mockResolvedValueOnce(jsonResponse(payload));

    const detail = await getVaultPokemon('3f4b9c1e-0000-4000-8000-000000000001');

    expect(fetchMock).toHaveBeenCalledWith('/api/vault/pokemon/3f4b9c1e-0000-4000-8000-000000000001', undefined);
    expect(detail.ivs.hp).toBe(31);
    expect(detail.evs.attack).toBe(252);
    expect(detail.moves).toHaveLength(4);
    expect(detail.moves[0].name).toBe('Thunderbolt');
  });

  it('maps GET /api/vault/pokemon/{id}/legality to a LegalityReport', async () => {
    const payload: LegalityReport = {
      valid: false,
      parsed: true,
      checks: [
        { identifier: 'Ball', severity: 'Invalid', valid: false, message: "Invalid: Can't have ball for encounter type." },
        { identifier: 'Level', severity: 'Valid', valid: true, message: 'Valid: Current level is not below met level.' },
        { identifier: 'Trainer', severity: 'Fishy', valid: true, message: 'Fishy: Suspicious Original Trainer details.' },
      ],
    };
    fetchMock.mockResolvedValueOnce(jsonResponse(payload));

    const report = await getVaultLegality('3f4b9c1e-0000-4000-8000-000000000001');

    expect(fetchMock).toHaveBeenCalledWith('/api/vault/pokemon/3f4b9c1e-0000-4000-8000-000000000001/legality', undefined);
    expect(report.valid).toBe(false);
    expect(report.parsed).toBe(true);
    expect(report.checks).toHaveLength(3);
    expect(report.checks[0].identifier).toBe('Ball');
    expect(report.checks[0].severity).toBe('Invalid');
    expect(report.checks[0].valid).toBe(false);
    expect(report.checks[0].message).toContain('ball');
  });

  it('throws ApiError with the server error message on failure', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ error: 'Box 0 slot 4 is empty — nothing to deposit.' }, 400));

    const err = await deposit({ saveId: 's1', box: 0, slot: 4 }).catch((e: unknown) => e);

    expect(err).toBeInstanceOf(ApiError);
    expect((err as ApiError).status).toBe(400);
    expect((err as ApiError).message).toContain('nothing to deposit');
  });
});
