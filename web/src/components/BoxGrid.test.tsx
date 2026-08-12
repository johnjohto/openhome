import { render, screen } from '@testing-library/react';
import { DndContext } from '@dnd-kit/core';
import { describe, expect, it } from 'vitest';
import { BoxGrid, BOX_SLOT_COUNT } from './BoxGrid';
import type { BoxSlotSummary } from '../api/types';

function slot(overrides: Partial<BoxSlotSummary>): BoxSlotSummary {
  return {
    box: 0,
    slot: 0,
    isEmpty: true,
    species: 0,
    form: 0,
    nickname: '',
    level: 0,
    isShiny: false,
    storedPokemonId: null,
    ...overrides,
  };
}

function renderGrid(slots: BoxSlotSummary[]) {
  return render(
    <DndContext>
      <BoxGrid slots={slots} makeSlotId={(s) => `test:${s}`} />
    </DndContext>,
  );
}

describe('BoxGrid', () => {
  it('renders exactly 30 cells', () => {
    renderGrid([]);
    const cells = screen.getAllByTestId(/^slot-/);
    expect(cells).toHaveLength(BOX_SLOT_COUNT);
    expect(BOX_SLOT_COUNT).toBe(30);
  });

  it('fills missing slots with empty placeholders', () => {
    renderGrid([slot({ slot: 3, isEmpty: false, species: 25, nickname: 'PIKACHU', level: 12 })]);

    expect(screen.getByTestId('slot-test:3')).toHaveAttribute('data-empty', 'false');
    expect(screen.getByTestId('slot-test:0')).toHaveAttribute('data-empty', 'true');
    expect(screen.getByTestId('slot-test:29')).toHaveAttribute('data-empty', 'true');
  });

  it('marks occupied slots as non-empty and shows level and shiny marker', () => {
    renderGrid([slot({ slot: 0, isEmpty: false, species: 6, nickname: 'CHAR', level: 50, isShiny: true })]);

    const cell = screen.getByTestId('slot-test:0');
    expect(cell).toHaveAttribute('data-empty', 'false');
    expect(cell).toHaveTextContent('50');
    expect(screen.getByLabelText('shiny')).toBeInTheDocument();
  });

  it('does not render a level badge for empty slots', () => {
    renderGrid([slot({ slot: 5, isEmpty: false, species: 1, nickname: 'BULBA', level: 5 })]);

    expect(screen.getByTestId('slot-test:4')).toHaveTextContent('');
    expect(screen.getByTestId('slot-test:5')).toHaveTextContent('5');
  });

  it('keeps row-major slot order (slot 0 first, slot 29 last)', () => {
    renderGrid([]);
    const cells = screen.getAllByTestId(/^slot-/);
    expect(cells[0]).toHaveAttribute('data-testid', 'slot-test:0');
    expect(cells[29]).toHaveAttribute('data-testid', 'slot-test:29');
  });
});
