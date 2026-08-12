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
  useMove,
  useSaveBoxes,
  useVaultBoxes,
  useWithdraw,
} from '../api/hooks';
import type { BoxSlotSummary, RegisteredSaveSummary } from '../api/types';
import { BoxGrid } from '../components/BoxGrid';
import { BoxSwitcher } from '../components/BoxSwitcher';
import { PokemonDetail, type SelectedSlot } from '../components/PokemonDetail';
import { PokemonSprite } from '../components/PokemonSprite';

// Drag ids encode the full location so drops resolve without extra state:
//   save slot:  save:<saveId>:<boxIndex>:<slot>
//   vault slot: vault:<boxId>:<slot>
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
  const deposit = useDeposit();
  const withdraw = useWithdraw();
  const move = useMove();
  const createBox = useCreateVaultBox();

  const [saveBoxIndex, setSaveBoxIndex] = useState(0);
  const [vaultBoxIndex, setVaultBoxIndex] = useState(0);
  const [selected, setSelected] = useState<SelectedSlot | null>(null);
  const [activeSlot, setActiveSlot] = useState<BoxSlotSummary | null>(null);

  const mutating = deposit.isPending || withdraw.isPending || move.isPending || createBox.isPending;
  const mutationError = deposit.error ?? withdraw.error ?? move.error ?? createBox.error;

  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 4 } }));

  const currentSaveBox = saveBoxes.data?.[Math.min(saveBoxIndex, (saveBoxes.data?.length ?? 1) - 1)];
  const currentVaultBox = vaultBoxes.data?.[Math.min(vaultBoxIndex, (vaultBoxes.data?.length ?? 1) - 1)];

  const selectedSlotId = useMemo(() => {
    if (!selected) return null;
    if (selected.side === 'vault') {
      return currentVaultBox ? `vault:${currentVaultBox.id}:${selected.slot.slot}` : null;
    }
    return currentSaveBox ? `save:${save.id}:${currentSaveBox.box}:${selected.slot.slot}` : null;
  }, [selected, currentVaultBox, currentSaveBox, save.id]);

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
      </div>

      {mutationError && (
        <p role="alert" className="mb-3 rounded border border-red-800 bg-red-950/60 px-3 py-2 text-sm text-red-200">
          {mutationError instanceof ApiError ? mutationError.message : 'Operation failed.'}
        </p>
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
                    onSelect={(slot) =>
                      setSelected({ side: 'save', slot, save, boxName: currentSaveBox.name })
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
                <div className="mt-3">
                  <BoxGrid
                    slots={currentVaultBox.slots}
                    makeSlotId={(slot) => `vault:${currentVaultBox.id}:${slot}`}
                    disabled={mutating}
                    selectedSlotId={selectedSlotId}
                    onSelect={(slot) =>
                      setSelected({ side: 'vault', slot, boxName: currentVaultBox.name })
                    }
                  />
                </div>
              </>
            )}
          </section>

          <PokemonDetail selected={selected} />
        </div>

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
        back to an empty save slot to withdraw; drag within the vault to reorganize.
      </p>
    </div>
  );
}
