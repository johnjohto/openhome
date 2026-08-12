import { useState } from 'react';

/**
 * Species sprite from /sprites/{species}.png (see tools/fetch-sprites.mjs).
 * Falls back to an inline SVG Poké Ball when the sprite hasn't been fetched.
 */
export function PokemonSprite({
  species,
  isShiny = false,
  size = 40,
  alt = '',
}: {
  species: number;
  isShiny?: boolean;
  size?: number;
  alt?: string;
}) {
  const [failed, setFailed] = useState(false);
  if (species <= 0 || failed) {
    return <PokeBallPlaceholder size={size} title={alt} />;
  }
  const src = isShiny ? `/sprites/${species}-shiny.png` : `/sprites/${species}.png`;
  return (
    <img
      src={src}
      width={size}
      height={size}
      alt={alt || `Species ${species}`}
      draggable={false}
      loading="lazy"
      className="pixelated select-none"
      style={{ width: size, height: size }}
      onError={() => setFailed(true)}
    />
  );
}

/** Inline SVG Poké Ball — the no-sprite fallback. */
export function PokeBallPlaceholder({ size = 40, title = 'Unknown Pokémon' }: { size?: number; title?: string }) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 32 32"
      role="img"
      aria-label={title}
      className="select-none opacity-70"
    >
      <title>{title}</title>
      <circle cx="16" cy="16" r="13" fill="#e2e8f0" stroke="#64748b" strokeWidth="2" />
      <path d="M3 16 A13 13 0 0 1 29 16 Z" fill="#ef4444" stroke="#64748b" strokeWidth="2" />
      <rect x="3" y="14.5" width="26" height="3" fill="#334155" />
      <circle cx="16" cy="16" r="4" fill="#e2e8f0" stroke="#334155" strokeWidth="2" />
      <circle cx="16" cy="16" r="1.8" fill="#f8fafc" />
    </svg>
  );
}
