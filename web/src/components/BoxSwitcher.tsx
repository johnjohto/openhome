/**
 * Box switcher shown above each box grid: ‹ › arrows, a select for jumping
 * straight to a box, and (optionally) a "new box" button.
 */
export function BoxSwitcher({
  boxes,
  currentIndex,
  onChange,
  onCreateBox,
  creating = false,
}: {
  boxes: { name: string }[];
  currentIndex: number;
  onChange: (index: number) => void;
  onCreateBox?: () => void;
  creating?: boolean;
}) {
  if (boxes.length === 0) {
    return <div className="text-sm text-slate-400">No boxes.</div>;
  }
  const current = Math.min(Math.max(currentIndex, 0), boxes.length - 1);
  return (
    <div className="flex items-center gap-2">
      <button
        type="button"
        aria-label="Previous box"
        disabled={current <= 0}
        onClick={() => onChange(current - 1)}
        className="rounded border border-slate-600 px-2 py-1 text-sm text-slate-200 hover:bg-slate-700 disabled:opacity-40"
      >
        ‹
      </button>
      <select
        aria-label="Box"
        value={current}
        onChange={(e) => onChange(Number(e.target.value))}
        className="min-w-0 flex-1 rounded border border-slate-600 bg-slate-800 px-2 py-1 text-sm text-slate-100"
      >
        {boxes.map((b, i) => (
          <option key={i} value={i}>
            {b.name}
          </option>
        ))}
      </select>
      <button
        type="button"
        aria-label="Next box"
        disabled={current >= boxes.length - 1}
        onClick={() => onChange(current + 1)}
        className="rounded border border-slate-600 px-2 py-1 text-sm text-slate-200 hover:bg-slate-700 disabled:opacity-40"
      >
        ›
      </button>
      {onCreateBox && (
        <button
          type="button"
          onClick={onCreateBox}
          disabled={creating}
          className="rounded border border-emerald-600 bg-emerald-900/40 px-2 py-1 text-sm whitespace-nowrap text-emerald-200 hover:bg-emerald-800/50 disabled:opacity-40"
        >
          + New box
        </button>
      )}
    </div>
  );
}
