import { useMemo, useState } from 'react';
import {
  DndContext,
  DragOverlay,
  PointerSensor,
  pointerWithin,
  useSensor,
  useSensors,
  type DragEndEvent,
  type DragStartEvent,
} from '@dnd-kit/core';
import { ApiError } from '../api/client';
import {
  useCreateVaultBox,
  useDeposit,
  useDepositMany,
  useMove,
  useMoveMany,
  useRelease,
  useSaveBoxes,
  useVaultBoxes,
  useVaultPokemon,
  useWithdraw,
} from '../api/hooks';
import type { BoxSlotRef, BoxSlotSummary, RegisteredSaveSummary, StoredPokemonSummary } from '../api/types';
import { BoxGrid } from '../components/BoxGrid';
import { BoxSwitcher } from '../components/BoxSwitcher';
import { PokemonDetail, type SelectedSlot } from '../components/PokemonDetail';
import { PokemonSprite } from '../components/PokemonSprite';
import {
  EMPTY_VAULT_FILTERS,
  isFilterActive,
  matchesVaultFilters,
  type VaultFilters,
} from '../components/vaultFilter';

// Drag ids encode the full location so drops resolve without extra state:
//   save slot:  save:<saveId>:<boxIndex>:<slot>
//   vault slot: vault:<boxId>:<slot>
// The same ids key the multi-select set, so selections survive box switches.
type ParsedSlotId =
  | { kind: 'save'; saveId: string; box: number; slot: number }
  | { kind: 'vault'; boxId: string; slot: number };

function parseSlotId(id: string): ParsedSlotId | null {
  const parts = id.split(':');
  if (parts[0] === 'save' && parts.length === 4) {
    return { kind: 'save', saveId: parts[1], box: Number(parts[2]), slot: Number(parts[3]) };
  }
  if (parts[0] === 'vault' && parts.length === 3) {
    return { kind: 'vault', boxId: parts[1], slot: Number(parts[2]) };
  }
  return null;
}

/** Side-by-side box browser: save on the left, vault on the right, drag between them. */
export function BoxBrowserPage({
  save,
  onBack,
}: {
  save: RegisteredSaveSummary;
  onBack: () => void;
}) {
  const saveBoxes = useSaveBoxes(save.id);
  const vaultBoxes = useVaultBoxes();
  const vaultPokemon = useVaultPokemon();
  const deposit = useDeposit();
  const withdraw = useWithdraw();
  const move = useMove();
  const createBox = useCreateVaultBox();
  const depositMany = useDepositMany();
  const moveMany = useMoveMany();
  const release = useRelease();

  const [saveBoxIndex, setSaveBoxIndex] = useState(0);
  const [vaultBoxIndex, setVaultBoxIndex] = useState(0);
  const [selected, setSelected] = useState<SelectedSlot | null>(null);
  const [activeSlot, setActiveSlot] = useState<BoxSlotSummary | null>(null);
  const [filters, setFilters] = useState<VaultFilters>(EMPTY_VAULT_FILTERS);
  const [multiMode, setMultiMode] = useState(false);
  const [selectedIds, setSelectedIds] = useState<ReadonlySet<string>>(new Set());
  const [moveTargetId, setMoveTargetId] = useState<string | null>(null);
  const [releaseReport, setReleaseReport] = useState<StoredPokemonSummary[] | null>(null);

  const mutating =
    deposit.isPending ||
    withdraw.isPending ||
    move.isPending ||
    createBox.isPending ||
    depositMany.isPending ||
    moveMany.isPending ||
    release.isPending;
  const mutationError =
    deposit.error ??
    withdraw.error ??
    move.error ??
    createBox.error ??
    depositMany.error ??
    moveMany.error ??
    release.error;

  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 4 } }));

  const currentSaveBox = saveBoxes.data?.[Math.min(saveBoxIndex, (saveBoxes.data?.length ?? 1) - 1)];
  const currentVaultBox = vaultBoxes.data?.[Math.min(vaultBoxIndex, (vaultBoxes.data?.length ?? 1) - 1)];

  const selectedSlotId = useMemo(() => {
    if (!selected || multiMode) return null;
    if (selected.side === 'vault') {
      return currentVaultBox ? `vault:${currentVaultBox.id}:${selected.slot.slot}` : null;
    }
    return currentSaveBox ? `save:${save.id}:${currentSaveBox.box}:${selected.slot.slot}` : null;
  }, [selected, multiMode, currentVaultBox, currentSaveBox, save.id]);

  // The stored-Pokémon index carries OT and origin game, which the slot grid does
  // not denormalize — the filter joins on storedPokemonId.
  const pokemonById = useMemo(
    () => new Map((vaultPokemon.data ?? []).map((p) => [p.id, p])),
    [vaultPokemon.data],
  );

  const originGames = useMemo(
    () => [...new Set((vaultPokemon.data ?? []).map((p) => p.originGame))].sort(),
    [vaultPokemon.data],
  );

  // Client-side filtering: the vault data is already fully loaded, so the grid is
  // dimmed in place with no server round-trip. (GET /api/vault/pokemon/query is
  // the server-side equivalent for API consumers and larger vaults.)
  const filtersActive = isFilterActive(filters);
  const { dimmedSlotIds, matchCount, occupiedCount } = useMemo(() => {
    const dimmed = new Set<string>();
    let matches = 0;
    let occupied = 0;
    if (currentVaultBox && filtersActive) {
      for (const slot of currentVaultBox.slots) {
        if (slot.isEmpty) continue;
        occupied++;
        const summary = slot.storedPokemonId ? pokemonById.get(slot.storedPokemonId) : undefined;
        if (matchesVaultFilters(slot, summary, filters)) matches++;
        else dimmed.add(`vault:${currentVaultBox.id}:${slot.slot}`);
      }
    }
    return { dimmedSlotIds: dimmed, matchCount: matches, occupiedCount: occupied };
  }, [currentVaultBox, filtersActive, filters, pokemonById]);

  // Resolve the multi-select id set into actionable refs, preserving click order.
  const selection = useMemo(() => {
    const saveSlots: BoxSlotRef[] = [];
    const vaultSlots: { id: string; slot: BoxSlotSummary }[] = [];
    for (const slotId of selectedIds) {
      const parsed = parseSlotId(slotId);
      if (!parsed) continue;
      if (parsed.kind === 'save') {
        if (parsed.saveId === save.id) saveSlots.push({ box: parsed.box, slot: parsed.slot });
      } else {
        const slot = vaultBoxes.data
          ?.find((b) => b.id === parsed.boxId)
          ?.slots.find((s) => s.slot === parsed.slot);
        if (slot?.storedPokemonId) vaultSlots.push({ id: slot.storedPokemonId, slot });
      }
    }
    return { saveSlots, vaultSlots };
  }, [selectedIds, save.id, vaultBoxes.data]);

  function clearSelection() {
    setSelectedIds(new Set());
  }

  function toggleSlot(slotId: string, slot: BoxSlotSummary) {
    if (slot.isEmpty) return;
    const next = new Set(selectedIds);
    if (next.has(slotId)) next.delete(slotId);
    else next.add(slotId);
    setSelectedIds(next);
    setSelected(null);
  }

  function depositSelected() {
    if (selection.saveSlots.length === 0) return;
    depositMany.mutate(
      { saveId: save.id, slots: selection.saveSlots },
      { onSuccess: clearSelection },
    );
  }

  function moveSelected() {
    const target = moveTargetId ?? currentVaultBox?.id;
    if (!target || selection.vaultSlots.length === 0) return;
    moveMany.mutate(
      { pokemonIds: selection.vaultSlots.map((s) => s.id), boxId: target },
      { onSuccess: clearSelection },
    );
  }

  function releaseSelected() {
    const names = selection.vaultSlots.map((s) => `${s.slot.nickname} (Lv.${s.slot.level})`);
    if (names.length === 0) return;
    if (!window.confirm(`Release these ${names.length} Pokémon? This is permanent.\n\n${names.join(', ')}`)) {
      return;
    }
    release.mutate(
      { pokemonIds: selection.vaultSlots.map((s) => s.id) },
      {
        onSuccess: (released) => {
          setReleaseReport(released);
          clearSelection();
        },
      },
    );
  }

  function onDragStart(event: DragStartEvent) {
    setActiveSlot((event.active.data.current as BoxSlotSummary | undefined) ?? null);
  }

  function onDragEnd(event: DragEndEvent) {
    setActiveSlot(null);
    const { active, over } = event;
    if (!over || active.id === over.id) return;
    const source = parseSlotId(String(active.id));
    const target = parseSlotId(String(over.id));
    if (!source || !target) return;
    const sourceSlot = active.data.current as BoxSlotSummary | undefined;
    const targetSlot = over.data.current as BoxSlotSummary | undefined;
    if (!sourceSlot || sourceSlot.isEmpty || !targetSlot) return;

    if (source.kind === 'save' && target.kind === 'vault') {
      // Deposit ignores the drop slot — the server picks the first free vault slot.
      deposit.mutate({ saveId: source.saveId, box: source.box, slot: source.slot });
    } else if (source.kind === 'vault' && target.kind === 'save') {
      if (!targetSlot.isEmpty) return;
      withdraw.mutate({
        pokemonId: sourceSlot.storedPokemonId as string,
        saveId: target.saveId,
        box: target.box,
        slot: target.slot,
      });
    } else if (source.kind === 'vault' && target.kind === 'vault') {
      if (!targetSlot.isEmpty) return;
      move.mutate({
        pokemonId: sourceSlot.storedPokemonId as string,
        boxId: target.boxId,
        slot: target.slot,
      });
    }
    // save → save moves are not an OpenHOME operation; ignore.
  }

  const filterSelectClass = 'rounded border border-slate-600 bg-slate-800 px-2 py-1 text-sm text-slate-100';

  return (
    <div className="mx-auto max-w-6xl p-4">
      <div className="mb-4 flex items-center gap-3">
        <button
          type="button"
          onClick={onBack}
          className="rounded border border-slate-600 px-3 py-1 text-sm text-slate-200 hover:bg-slate-700"
        >
          ← Library
        </button>
        <h1 className="text-xl font-bold text-slate-100">
          {save.game} <span className="font-normal text-slate-400">· {save.trainerName}</span>
        </h1>
        <button
          type="button"
          aria-pressed={multiMode}
          onClick={() => {
            setMultiMode((m) => !m);
            clearSelection();
          }}
          className={[
            'ml-auto rounded border px-3 py-1 text-sm',
            multiMode
              ? 'border-amber-500 bg-amber-900/40 text-amber-200 hover:bg-amber-800/50'
              : 'border-slate-600 text-slate-200 hover:bg-slate-700',
          ].join(' ')}
        >
          {multiMode ? 'Done selecting' : 'Select multiple'}
        </button>
      </div>

      {mutationError && (
        <p role="alert" className="mb-3 rounded border border-red-800 bg-red-950/60 px-3 py-2 text-sm text-red-200">
          {mutationError instanceof ApiError ? mutationError.message : 'Operation failed.'}
        </p>
      )}

      {releaseReport && (
        <div
          role="status"
          className="mb-3 flex items-start gap-3 rounded border border-emerald-800 bg-emerald-950/60 px-3 py-2 text-sm text-emerald-200"
        >
          <span className="min-w-0 flex-1">
            Released {releaseReport.length}{' '}
            {releaseReport.length === 1 ? 'Pokémon' : 'Pokémon'}:{' '}
            {releaseReport.map((r) => `${r.nickname} (Lv.${r.level}, ${r.boxName})`).join(', ')}
          </span>
          <button
            type="button"
            aria-label="Dismiss release report"
            onClick={() => setReleaseReport(null)}
            className="rounded px-1 text-emerald-300 hover:bg-emerald-900/60"
          >
            ×
          </button>
        </div>
      )}

      <DndContext
        sensors={sensors}
        collisionDetection={pointerWithin}
        onDragStart={onDragStart}
        onDragEnd={onDragEnd}
        onDragCancel={() => setActiveSlot(null)}
      >
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-[1fr_1fr_260px]">
          <section aria-label="Save boxes" className="rounded-xl border border-slate-700 bg-slate-900/40 p-4">
            <h2 className="mb-3 text-sm font-semibold tracking-wide text-slate-300 uppercase">Save</h2>
            {saveBoxes.isPending && <p className="text-sm text-slate-400">Loading boxes…</p>}
            {saveBoxes.isError && (
              <p role="alert" className="text-sm text-red-300">
                Failed to load save boxes.
              </p>
            )}
            {saveBoxes.data && currentSaveBox && (
              <>
                <BoxSwitcher boxes={saveBoxes.data} currentIndex={saveBoxIndex} onChange={setSaveBoxIndex} />
                <div className="mt-3">
                  <BoxGrid
                    slots={currentSaveBox.slots}
                    makeSlotId={(slot) => `save:${save.id}:${currentSaveBox.box}:${slot}`}
                    disabled={mutating}
                    selectedSlotId={selectedSlotId}
                    selectedSlotIds={multiMode ? selectedIds : null}
                    onSelect={(slot) =>
                      multiMode
                        ? toggleSlot(`save:${save.id}:${currentSaveBox.box}:${slot.slot}`, slot)
                        : setSelected({ side: 'save', slot, save, boxName: currentSaveBox.name })
                    }
                  />
                </div>
              </>
            )}
          </section>

          <section aria-label="Vault boxes" className="rounded-xl border border-slate-700 bg-slate-900/40 p-4">
            <h2 className="mb-3 text-sm font-semibold tracking-wide text-slate-300 uppercase">Vault</h2>
            {vaultBoxes.isPending && <p className="text-sm text-slate-400">Loading boxes…</p>}
            {vaultBoxes.isError && (
              <p role="alert" className="text-sm text-red-300">
                Failed to load vault boxes.
              </p>
            )}
            {vaultBoxes.data && currentVaultBox && (
              <>
                <BoxSwitcher
                  boxes={vaultBoxes.data}
                  currentIndex={vaultBoxIndex}
                  onChange={setVaultBoxIndex}
                  onCreateBox={() => createBox.mutate(undefined)}
                  creating={createBox.isPending}
                />
                <div className="mt-3 flex flex-wrap items-center gap-2">
                  <input
                    type="search"
                    aria-label="Search vault"
                    placeholder="Search nickname, OT or species #…"
                    value={filters.text}
                    onChange={(e) => setFilters({ ...filters, text: e.target.value })}
                    className="min-w-0 flex-1 rounded border border-slate-600 bg-slate-800 px-2 py-1 text-sm text-slate-100 placeholder:text-slate-500"
                  />
                  <select
                    aria-label="Shiny filter"
                    value={filters.shiny}
                    onChange={(e) => setFilters({ ...filters, shiny: e.target.value as VaultFilters['shiny'] })}
                    className={filterSelectClass}
                  >
                    <option value="any">Shiny: any</option>
                    <option value="shiny">Shiny</option>
                    <option value="normal">Not shiny</option>
                  </select>
                  <select
                    aria-label="Legality filter"
                    value={filters.legality}
                    onChange={(e) => setFilters({ ...filters, legality: e.target.value as VaultFilters['legality'] })}
                    className={filterSelectClass}
                  >
                    <option value="any">Legality: any</option>
                    <option value="valid">Legal</option>
                    <option value="invalid">Illegal</option>
                  </select>
                  <select
                    aria-label="Origin game filter"
                    value={filters.originGame}
                    onChange={(e) => setFilters({ ...filters, originGame: e.target.value })}
                    className={filterSelectClass}
                  >
                    <option value="any">Origin: any</option>
                    {originGames.map((game) => (
                      <option key={game} value={game}>
                        {game}
                      </option>
                    ))}
                  </select>
                  {filtersActive && (
                    <>
                      <span className="text-xs text-slate-400">
                        {matchCount}/{occupiedCount} match
                      </span>
                      <button
                        type="button"
                        onClick={() => setFilters(EMPTY_VAULT_FILTERS)}
                        className="rounded border border-slate-600 px-2 py-1 text-xs text-slate-300 hover:bg-slate-700"
                      >
                        Clear
                      </button>
                    </>
                  )}
                </div>
                <div className="mt-3">
                  <BoxGrid
                    slots={currentVaultBox.slots}
                    makeSlotId={(slot) => `vault:${currentVaultBox.id}:${slot}`}
                    disabled={mutating}
                    selectedSlotId={selectedSlotId}
                    selectedSlotIds={multiMode ? selectedIds : null}
                    dimmedSlotIds={dimmedSlotIds}
                    onSelect={(slot) =>
                      multiMode
                        ? toggleSlot(`vault:${currentVaultBox.id}:${slot.slot}`, slot)
                        : setSelected({ side: 'vault', slot, boxName: currentVaultBox.name })
                    }
                  />
                </div>
              </>
            )}
          </section>

          <PokemonDetail selected={selected} />
        </div>

        {multiMode && selectedIds.size > 0 && (
          <div
            aria-label="Bulk actions"
            className="mt-4 flex flex-wrap items-center gap-3 rounded-xl border border-slate-700 bg-slate-900/60 px-4 py-3"
          >
            <span className="text-sm text-slate-300">
              {selectedIds.size} selected ({selection.saveSlots.length} in save · {selection.vaultSlots.length} in
              vault)
            </span>
            <button
              type="button"
              disabled={selection.saveSlots.length === 0 || mutating}
              onClick={depositSelected}
              className="rounded border border-sky-600 bg-sky-900/40 px-3 py-1 text-sm text-sky-200 hover:bg-sky-800/50 disabled:opacity-40"
            >
              Deposit {selection.saveSlots.length || ''} → vault
            </button>
            <select
              aria-label="Move target box"
              value={moveTargetId ?? currentVaultBox?.id ?? ''}
              onChange={(e) => setMoveTargetId(e.target.value)}
              className={filterSelectClass}
            >
              {vaultBoxes.data?.map((box) => (
                <option key={box.id} value={box.id}>
                  {box.name}
                </option>
              ))}
            </select>
            <button
              type="button"
              disabled={selection.vaultSlots.length === 0 || mutating}
              onClick={moveSelected}
              className="rounded border border-slate-500 bg-slate-800 px-3 py-1 text-sm text-slate-200 hover:bg-slate-700 disabled:opacity-40"
            >
              Move {selection.vaultSlots.length || ''} → box
            </button>
            <button
              type="button"
              disabled={selection.vaultSlots.length === 0 || mutating}
              onClick={releaseSelected}
              className="rounded border border-red-700 bg-red-900/40 px-3 py-1 text-sm text-red-200 hover:bg-red-800/50 disabled:opacity-40"
            >
              Release {selection.vaultSlots.length || ''}…
            </button>
            <button
              type="button"
              onClick={clearSelection}
              className="ml-auto rounded border border-slate-600 px-2 py-1 text-xs text-slate-300 hover:bg-slate-700"
            >
              Clear
            </button>
          </div>
        )}

        <DragOverlay>
          {activeSlot && !activeSlot.isEmpty && (
            <div className="rounded-lg border border-sky-400 bg-slate-800/90 p-1 shadow-xl">
              <PokemonSprite species={activeSlot.species} isShiny={activeSlot.isShiny} alt={activeSlot.nickname} />
            </div>
          )}
        </DragOverlay>
      </DndContext>

      <p className="mt-4 text-xs text-slate-500">
        Drag a Pokémon from the save to the vault to deposit it (the server picks the first free slot); drag
        back to an empty save slot to withdraw; drag within the vault to reorganize. Use the search and filters
        to dim non-matching vault slots in place, or “Select multiple” to click-select Pokémon for bulk
        deposit, move, or release.
      </p>
    </div>
  );
}
