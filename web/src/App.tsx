import { useState } from 'react';
import { useConfig } from './api/hooks';
import type { RegisteredSaveSummary } from './api/types';
import { BoxBrowserPage } from './pages/BoxBrowserPage';
import { DexPage } from './pages/DexPage';
import { SaveLibraryPage } from './pages/SaveLibraryPage';

type Page = 'library' | 'dex';

export default function App() {
  const [page, setPage] = useState<Page>('library');
  const [openSave, setOpenSave] = useState<RegisteredSaveSummary | null>(null);
  const config = useConfig();

  const navLink = (target: Page, label: string) => (
    <button
      type="button"
      onClick={() => {
        setPage(target);
        setOpenSave(null);
      }}
      className={[
        'rounded px-2 py-1 text-sm',
        page === target ? 'bg-slate-800 text-slate-100' : 'text-slate-400 hover:text-slate-200',
      ].join(' ')}
    >
      {label}
    </button>
  );

  return (
    <div className="min-h-screen bg-slate-950 text-slate-100">
      <header className="flex items-center border-b border-slate-800 bg-slate-900/60 px-6 py-3">
        <span className="text-lg font-bold tracking-tight">
          Open<span className="text-sky-400">HOME</span>
        </span>
        <span className="ml-3 text-xs text-slate-500">self-hosted Pokémon storage</span>
        <nav className="ml-6 flex gap-1">
          {navLink('library', 'Library')}
          {navLink('dex', 'Pokédex')}
        </nav>
        {config.data && (
          <span
            className={[
              'ml-auto rounded-full border px-2 py-0.5 text-xs',
              config.data.strictTransfers
                ? 'border-amber-600 bg-amber-900/40 text-amber-200'
                : 'border-slate-600 bg-slate-800 text-slate-300',
            ].join(' ')}
            title={
              config.data.strictTransfers
                ? 'Strict transfer mode: withdraws the target game cannot legally receive are refused.'
                : 'Free transfer mode: withdraws always proceed; transfer-legality issues come back as warnings.'
            }
          >
            Transfers: {config.data.strictTransfers ? 'strict' : 'free'}
          </span>
        )}
      </header>
      <main>
        {page === 'dex' ? (
          <DexPage />
        ) : openSave ? (
          <BoxBrowserPage save={openSave} onBack={() => setOpenSave(null)} />
        ) : (
          <SaveLibraryPage onOpenSave={setOpenSave} />
        )}
      </main>
    </div>
  );
}
