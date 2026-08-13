import type { BoxSlotSummary } from '../api/types';
import { BoxSlotCell } from './BoxSlotCell';

export const BOX_COLUMNS = 6;
export const BOX_ROWS = 5;
export const BOX_SLOT_COUNT = BOX_COLUMNS * BOX_ROWS;

/**
 * A 6×5 (30-slot) box grid. Slot order matches the API: row-major,
 * slot 0 top-left … slot 29 bottom-right.
 */
export function BoxGrid({
  slots,
  makeSlotId,
  disabled = false,
  selectedSlotId = null,
  selectedSlotIds = null,
  dimmedSlotIds = null,
  onSelect,
}: {
  slots: BoxSlotSummary[];
  makeSlotId: (slot: number) => string;
  disabled?: boolean;
  /** Single highlighted slot (detail pane selection). */
  selectedSlotId?: string | null;
  /** Multi-select: every id in the set renders highlighted. */
  selectedSlotIds?: ReadonlySet<string> | null;
  /** Slots dimmed out by an active filter (they stay interactive). */
  dimmedSlotIds?: ReadonlySet<string> | null;
  onSelect?: (slot: BoxSlotSummary) => void;
}) {
  const bySlot = new Map(slots.map((s) => [s.slot, s]));
  const cells: BoxSlotSummary[] = [];
  for (let i = 0; i < BOX_SLOT_COUNT; i++) {
    cells.push(
      bySlot.get(i) ?? {
        box: 0,
        slot: i,
        isEmpty: true,
        species: 0,
        form: 0,
        nickname: '',
        level: 0,
        isShiny: false,
        storedPokemonId: null,
        legalityValid: null,
      },
    );
  }

  return (
    <div className="grid grid-cols-6 gap-1.5" role="grid" aria-label="Pokémon box">
      {cells.map((slot) => {
        const slotId = makeSlotId(slot.slot);
        return (
          <BoxSlotCell
            key={slot.slot}
            slotId={slotId}
            slot={slot}
            disabled={disabled}
            selected={selectedSlotId === slotId || (selectedSlotIds?.has(slotId) ?? false)}
            dimmed={dimmedSlotIds?.has(slotId) ?? false}
            onSelect={onSelect}
          />
        );
      })}
    </div>
  );
}
