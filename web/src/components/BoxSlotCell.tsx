import { useDraggable, useDroppable } from '@dnd-kit/core';
import type { BoxSlotSummary } from '../api/types';
import { PokemonSprite } from './PokemonSprite';

/**
 * One cell of a 6×5 box grid. Occupied cells are draggable; every cell is a
 * drop target. `slotId` is the fully-qualified dnd id (e.g. `vault:<boxId>:4`
 * or `save:<saveId>:2:7`) so the drag handler can resolve source and target.
 */
export function BoxSlotCell({
  slotId,
  slot,
  disabled = false,
  selected = false,
  onSelect,
}: {
  slotId: string;
  slot: BoxSlotSummary;
  disabled?: boolean;
  selected?: boolean;
  onSelect?: (slot: BoxSlotSummary) => void;
}) {
  const { attributes, listeners, setNodeRef: setDragRef, isDragging } = useDraggable({
    id: slotId,
    data: slot,
    disabled: disabled || slot.isEmpty,
  });
  const { isOver, setNodeRef: setDropRef } = useDroppable({
    id: slotId,
    data: slot,
    disabled,
  });

  return (
    <div
      ref={setDropRef}
      data-testid={`slot-${slotId}`}
      data-empty={slot.isEmpty}
      className={[
        'relative flex aspect-square items-center justify-center rounded-md border transition-colors',
        slot.isEmpty
          ? 'border-slate-700/60 bg-slate-900/60'
          : 'cursor-grab border-slate-600 bg-slate-800 hover:border-sky-400',
        isOver ? 'border-emerald-400 bg-emerald-900/40 ring-2 ring-emerald-400' : '',
        isDragging ? 'opacity-30' : '',
        selected ? 'ring-2 ring-amber-400' : '',
      ].join(' ')}
    >
      {slot.isEmpty ? (
        <span className="h-1.5 w-1.5 rounded-full bg-slate-700" aria-hidden />
      ) : (
        <button
          ref={setDragRef}
          type="button"
          {...listeners}
          {...attributes}
          onClick={() => onSelect?.(slot)}
          className="flex h-full w-full cursor-grab items-center justify-center active:cursor-grabbing"
          title={`${slot.nickname} — Lv.${slot.level}${slot.isShiny ? ' ★' : ''}`}
        >
          <PokemonSprite species={slot.species} isShiny={slot.isShiny} alt={slot.nickname} />
          {slot.isShiny && (
            <span className="absolute top-0.5 right-1 text-[10px] text-amber-300" aria-label="shiny">
              ★
            </span>
          )}
          {slot.legalityValid !== null && (
            <span
              className={[
                'absolute top-0.5 left-1 text-[10px] leading-none',
                slot.legalityValid ? 'text-emerald-400' : 'text-rose-400',
              ].join(' ')}
              aria-label={slot.legalityValid ? 'legality: valid' : 'legality: invalid'}
              title={slot.legalityValid ? 'Legality: valid' : 'Legality: invalid — select to view the report'}
            >
              {slot.legalityValid ? '✓' : '✗'}
            </span>
          )}
          <span className="absolute right-1 bottom-0.5 rounded bg-slate-950/80 px-1 text-[9px] text-slate-200">
            {slot.level}
          </span>
        </button>
      )}
    </div>
  );
}
