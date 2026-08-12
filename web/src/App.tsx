import { useState } from 'react';
import type { RegisteredSaveSummary } from './api/types';
import { BoxBrowserPage } from './pages/BoxBrowserPage';
import { SaveLibraryPage } from './pages/SaveLibraryPage';

export default function App() {
  const [openSave, setOpenSave] = useState<RegisteredSaveSummary | null>(null);

  return (
    <div className="min-h-screen bg-slate-950 text-slate-100">
      <header className="border-b border-slate-800 bg-slate-900/60 px-6 py-3">
        <span className="text-lg font-bold tracking-tight">
          Open<span className="text-sky-400">HOME</span>
        </span>
        <span className="ml-3 text-xs text-slate-500">self-hosted Pokémon storage</span>
      </header>
      <main>
        {openSave ? (
          <BoxBrowserPage save={openSave} onBack={() => setOpenSave(null)} />
        ) : (
          <SaveLibraryPage onOpenSave={setOpenSave} />
        )}
      </main>
    </div>
  );
}
