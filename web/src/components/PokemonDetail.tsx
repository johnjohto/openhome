import type { BoxSlotSummary } from '../api/types';
import type { RegisteredSaveSummary, StoredPokemonDetail } from '../api/types';
import { useVaultPokemonDetail } from '../api/hooks';
import { PokemonSprite } from './PokemonSprite';

export interface SelectedSlot {
  /** Which panel the slot lives in. */
  side: 'save' | 'vault';
  slot: BoxSlotSummary;
  save?: RegisteredSaveSummary | null;
  boxName?: string;
}

/** Right-hand detail panel. Vault slots render the full record from GET /api/vault/pokemon/{id}. */
export function PokemonDetail({ selected }: { selected: SelectedSlot | null }) {
  if (!selected) {
    return (
      <aside className="rounded-lg border border-slate-700 bg-slate-900/50 p-4 text-sm text-slate-400">
        Select a Pokémon to see its details.
      </aside>
    );
  }
  const { slot, save, boxName } = selected;
  return (
    <aside className="rounded-lg border border-slate-700 bg-slate-900/50 p-4" aria-label="Pokémon details">
      <div className="flex items-center gap-3">
        <div className="rounded-lg bg-slate-800 p-2">
          <PokemonSprite species={slot.species} isShiny={slot.isShiny} size={64} alt={slot.nickname} />
        </div>
        <div>
          <div className="text-lg font-semibold text-slate-100">
            {slot.nickname}
            {slot.isShiny && <span className="ml-1 text-amber-300">★</span>}
          </div>
          <div className="text-sm text-slate-400">
            {selected.side === 'vault' ? 'Vault' : (save?.game ?? 'Save')}
            {boxName ? ` · ${boxName}` : ''} · Slot {slot.slot + 1}
          </div>
        </div>
      </div>
      <dl className="mt-4 grid grid-cols-2 gap-x-4 gap-y-2 text-sm">
        <Row label="Species #">{slot.species}</Row>
        <Row label="Form">{slot.form}</Row>
        <Row label="Level">{slot.level}</Row>
        <Row label="Shiny">{slot.isShiny ? 'Yes ★' : 'No'}</Row>
        {save && (
          <>
            <Row label="Origin save">{save.game}</Row>
            <Row label="Trainer">{save.trainerName}</Row>
          </>
        )}
      </dl>
      {selected.side === 'vault' && slot.storedPokemonId && (
        <VaultDetail storedPokemonId={slot.storedPokemonId} />
      )}
    </aside>
  );
}

/** The stored record: OT, origin game, HOME tracker, IVs, EVs and moves. */
function VaultDetail({ storedPokemonId }: { storedPokemonId: string }) {
  const { data, isLoading, isError, error } = useVaultPokemonDetail(storedPokemonId);

  if (isLoading) {
    return <p className="mt-4 text-xs text-slate-500">Reading the vault record…</p>;
  }
  if (isError) {
    return <p className="mt-4 text-xs text-rose-400">{error.message}</p>;
  }
  if (!data) {
    return null;
  }
  return (
    <div className="mt-4 border-t border-slate-700 pt-3">
      <dl className="grid grid-cols-2 gap-x-4 gap-y-2 text-sm">
        <Row label="OT">{data.otName}</Row>
        <Row label="Origin game">{data.originGame}</Row>
        <Row label="HOME tracker" wide>
          <span className="font-mono text-xs">{data.homeTracker}</span>
        </Row>
      </dl>
      <StatTable label="IVs" stats={data.ivs} />
      <StatTable label="EVs" stats={data.evs} />
      <div className="mt-3">
        <dt className="text-sm text-slate-500">Moves</dt>
        <dd className="mt-1 grid grid-cols-2 gap-x-4 gap-y-1 text-sm text-slate-200">
          {data.moves.map((m) => (
            <span key={m.id}>{m.id === 0 ? '—' : m.name || `#${m.id}`}</span>
          ))}
        </dd>
      </div>
    </div>
  );
}

function StatTable({ label, stats }: { label: string; stats: StoredPokemonDetail['ivs'] }) {
  return (
    <div className="mt-3">
      <dt className="text-sm text-slate-500">{label}</dt>
      <dd className="mt-1 grid grid-cols-6 gap-1 text-center text-xs">
        <span title="HP">HP {stats.hp}</span>
        <span title="Attack">Atk {stats.attack}</span>
        <span title="Defense">Def {stats.defense}</span>
        <span title="Special Attack">SpA {stats.spAttack}</span>
        <span title="Special Defense">SpD {stats.spDefense}</span>
        <span title="Speed">Spe {stats.speed}</span>
      </dd>
    </div>
  );
}

function Row({ label, wide = false, children }: { label: string; wide?: boolean; children: React.ReactNode }) {
  return (
    <div className={wide ? 'col-span-2' : ''}>
      <dt className="text-slate-500">{label}</dt>
      <dd className="text-slate-200">{children}</dd>
    </div>
  );
}
