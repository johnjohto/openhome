import { useRef, useState } from 'react';
import { ApiError } from '../api/client';
import { useSaves, useUploadSave } from '../api/hooks';
import type { RegisteredSaveSummary } from '../api/types';

/** Save library: upload saves (drag or picker) and pick one to browse. */
export function SaveLibraryPage({ onOpenSave }: { onOpenSave: (save: RegisteredSaveSummary) => void }) {
  const saves = useSaves();
  const upload = useUploadSave();
  const inputRef = useRef<HTMLInputElement>(null);
  const [dragOver, setDragOver] = useState(false);

  const sendFile = (file: File | undefined) => {
    if (!file || upload.isPending) return;
    upload.mutate(file);
  };

  return (
    <div className="mx-auto max-w-3xl p-6">
      <h1 className="text-2xl font-bold text-slate-100">Save Library</h1>
      <p className="mt-1 text-sm text-slate-400">
        Register save dumps (Checkpoint/JKSM, emulator <code>.sav</code>/<code>.dsv</code>, flashcart) to browse
        their boxes and move Pokémon into the vault.
      </p>

      <div
        role="button"
        tabIndex={0}
        aria-label="Upload save file"
        onClick={() => inputRef.current?.click()}
        onKeyDown={(e) => e.key === 'Enter' && inputRef.current?.click()}
        onDragOver={(e) => {
          e.preventDefault();
          setDragOver(true);
        }}
        onDragLeave={() => setDragOver(false)}
        onDrop={(e) => {
          e.preventDefault();
          setDragOver(false);
          sendFile(e.dataTransfer.files[0]);
        }}
        className={[
          'mt-6 flex cursor-pointer flex-col items-center justify-center rounded-xl border-2 border-dashed p-10 text-center transition-colors',
          dragOver ? 'border-sky-400 bg-sky-950/40' : 'border-slate-600 bg-slate-900/40 hover:border-slate-400',
        ].join(' ')}
      >
        <span className="text-lg text-slate-200">
          {upload.isPending ? 'Uploading…' : 'Drop a save file here, or click to browse'}
        </span>
        <span className="mt-1 text-xs text-slate-500">The file is copied into the server's data directory.</span>
        <input
          ref={inputRef}
          type="file"
          className="hidden"
          onChange={(e) => {
            sendFile(e.target.files?.[0]);
            e.target.value = '';
          }}
        />
      </div>

      {upload.isError && (
        <p role="alert" className="mt-3 rounded border border-red-800 bg-red-950/60 px-3 py-2 text-sm text-red-200">
          {upload.error instanceof ApiError ? upload.error.message : 'Upload failed.'}
        </p>
      )}

      <h2 className="mt-8 mb-3 text-lg font-semibold text-slate-200">Registered saves</h2>
      {saves.isPending && <p className="text-sm text-slate-400">Loading…</p>}
      {saves.isError && (
        <p role="alert" className="text-sm text-red-300">
          Could not reach the server — is OpenHome.Server running on port 5140?
        </p>
      )}
      {saves.data && saves.data.length === 0 && (
        <p className="text-sm text-slate-400">No saves registered yet.</p>
      )}
      <ul className="space-y-2">
        {saves.data?.map((save) => (
          <li key={save.id}>
            <button
              type="button"
              onClick={() => onOpenSave(save)}
              className="flex w-full items-center justify-between rounded-lg border border-slate-700 bg-slate-900/60 px-4 py-3 text-left hover:border-sky-500"
            >
              <div>
                <div className="font-medium text-slate-100">{save.game}</div>
                <div className="text-sm text-slate-400">
                  {save.trainerName} · {save.fileName}
                </div>
              </div>
              <div className="text-right text-xs text-slate-500">
                Registered {new Date(save.registeredAt).toLocaleDateString()}
              </div>
            </button>
          </li>
        ))}
      </ul>
    </div>
  );
}
