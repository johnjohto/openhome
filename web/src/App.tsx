import { useState } from 'react';
import type { RegisteredSaveSummary } from './api/types';
import { BoxBrowserPage } from './pages/BoxBrowserPage';
import { DexPage } from './pages/DexPage';
import { SaveLibraryPage } from './pages/SaveLibraryPage';

type Page = 'library' | 'dex';

export default function App() {
  const [page, setPage] = useState<Page>('library');
  const [openSave, setOpenSave] = useState<RegisteredSaveSummary | null>(null);

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
