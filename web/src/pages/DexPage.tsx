import { useMemo } from 'react';
import { useNationalDex, useSaveDex, useSaves } from '../api/hooks';
import { buildDexCells, completionPercent } from '../components/dexGrid';

/** Living dex: national progress from the vault, per-save progress, and the owned/missing grid. */
export function DexPage() {
  const national = useNationalDex();
  const saves = useSaves();
  const cells = useMemo(() => (national.data ? buildDexCells(national.data) : []), [national.data]);

  return (
    <div className="mx-auto max-w-6xl p-6">
      <h1 className="text-2xl font-bold text-slate-100">Pokédex</h1>
      <p className="mt-1 text-sm text-slate-400">
        Living dex progress from the vault, plus each registered save's own dex.
      </p>

      {national.isPending && <p className="mt-6 text-sm text-slate-400">Loading…</p>}
      {national.isError && (
        <p role="alert" className="mt-6 text-sm text-red-300">
          Could not reach the server — is OpenHome.Server running on port 5140?
        </p>
      )}

      {national.data && (
        <section className="mt-6 rounded-xl border border-slate-700 bg-slate-900/60 p-4">
          <div className="flex items-baseline justify-between">
            <h2 className="text-lg font-semibold text-slate-200">National dex (vault)</h2>
            <span className="text-sm text-slate-400">
              {national.data.owned} / {national.data.total} species ·{' '}
              <span className="text-amber-300">★ {national.data.shinyOwned} shiny</span>
            </span>
          </div>
          <ProgressBar percent={completionPercent(national.data.owned, national.data.total)} />
        </section>
      )}

      {saves.data && saves.data.length > 0 && (
        <section className="mt-6">
          <h2 className="mb-3 text-lg font-semibold text-slate-200">Save dexes</h2>
          <ul className="grid gap-3 sm:grid-cols-2">
            {saves.data.map((save) => (
              <SaveDexCard key={save.id} saveId={save.id} game={save.game} trainerName={save.trainerName} />
            ))}
          </ul>
        </section>
      )}

      {national.data && (
        <section className="mt-8">
          <h2 className="mb-3 text-lg font-semibold text-slate-200">Species</h2>
          <div className="grid grid-cols-4 gap-1.5 sm:grid-cols-6 md:grid-cols-8 lg:grid-cols-10">
            {cells.map((cell) => (
              <div
                key={cell.species}
                title={
                  cell.owned
                    ? `#${cell.number} ${cell.name}${cell.formCount > 1 ? ` · ${cell.formCount} forms` : ''}${cell.shinyOwned ? ' · shiny owned' : ''}`
                    : `#${cell.number} ${cell.name} — missing`
                }
                className={[
                  'flex flex-col items-center rounded border px-1 py-1.5 text-center',
                  cell.owned
                    ? 'border-sky-700 bg-sky-950/50 text-slate-100'
                    : 'border-slate-800 bg-slate-900/40 text-slate-600',
                ].join(' ')}
              >
                <span className="text-[10px] leading-none text-slate-500">#{cell.number}</span>
                <span className="mt-0.5 w-full truncate text-xs leading-tight">
                  {cell.shinyOwned && <span className="mr-0.5 text-amber-300">★</span>}
                  {cell.name}
                </span>
                {cell.formCount > 1 && (
                  <span className="mt-0.5 rounded bg-slate-800 px-1 text-[10px] leading-tight text-slate-300">
                    {cell.formCount} forms
                  </span>
                )}
              </div>
            ))}
          </div>
        </section>
      )}
    </div>
  );
}

function SaveDexCard({ saveId, game, trainerName }: { saveId: string; game: string; trainerName: string }) {
  const dex = useSaveDex(saveId);

  return (
    <li className="rounded-lg border border-slate-700 bg-slate-900/60 p-4">
      <div className="flex items-baseline justify-between">
        <div>
          <div className="font-medium text-slate-100">{game}</div>
          <div className="text-xs text-slate-500">{trainerName}</div>
        </div>
        {dex.data && (
          <span className="text-sm text-slate-400">
            {dex.data.caught} / {dex.data.total} caught
          </span>
        )}
      </div>
      {dex.isPending && <p className="mt-2 text-xs text-slate-500">Loading…</p>}
      {dex.isError && <p className="mt-2 text-xs text-red-300">Could not load this save's dex.</p>}
      {dex.data && (
        <>
          <ProgressBar percent={completionPercent(dex.data.caught, dex.data.total)} />
          <p className="mt-2 text-xs text-slate-500">
            {dex.data.seen} seen
            {!dex.data.usesSaveDexData && ' · no Pokédex on this save — counted from box contents'}
          </p>
        </>
      )}
    </li>
  );
}

function ProgressBar({ percent }: { percent: number }) {
  return (
    <div className="mt-3 flex items-center gap-2">
      <div className="h-2 flex-1 overflow-hidden rounded-full bg-slate-800">
        <div className="h-full rounded-full bg-sky-500 transition-all" style={{ width: `${percent}%` }} />
      </div>
      <span className="w-12 text-right text-xs text-slate-400">{percent}%</span>
    </div>
  );
}
