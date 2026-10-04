import React from 'react';

/**
 * The Pdf mark from the design system: the codex with its right page dog-eared, in its full drawing (the 404 page
 * draws it at 64px, above the 48px from which the full drawing applies). The strokes follow the color scheme in
 * fg-strong; the braces and the fold take the accent, or fg-strong too with `mono`, as pdf-symbol-mono.svg does.
 */
export default function PdfMark({ size = 64, mono = false }) {
  const ink = 'var(--fg-strong)';
  const accent = mono ? ink : 'var(--accent)';
  const round = { strokeLinejoin: 'round', strokeLinecap: 'round' };
  return (
    <svg viewBox="0 0 100 100" width={size} height={size} role="img" aria-label="AdCodicem.Pdf">
      <path d="M8 18 L8 71 L41 80 C 44 86 56 86 59 80 L92 71 L92 18" fill="none" stroke={ink} strokeWidth="4.6" {...round} />
      <line x1="79.08" y1="20.46" x2="92" y2="18" stroke={ink} strokeWidth="3.8" {...round} />
      <path d="M50 26 C 38 15 24 11 12 15 L12 68 C 24 64 38 68 50 79" fill="none" stroke={ink} strokeWidth="3.8" {...round} />
      <path
        d="M50 26 C 57.23 19.37 65.19 15.28 73 13.96 L88 30 L88 68 C 76 64 62 68 50 79"
        fill="none"
        stroke={ink}
        strokeWidth="3.8"
        {...round}
      />
      <line x1="50" y1="26" x2="50" y2="80" stroke={ink} strokeWidth="3.8" />
      <path d="M12.8 72 C 24 67.8 38 71.4 50 79" fill="none" stroke={ink} strokeWidth="1.9" {...round} />
      <path d="M87.2 72 C 76 67.8 62 71.4 50 79" fill="none" stroke={ink} strokeWidth="1.9" {...round} />
      <polygon points="73,13.96 88,30 73.03,28.99" fill={accent} stroke={accent} strokeWidth="1.6" strokeLinejoin="round" />
      <polygon points="73,13.96 88,30 73.03,28.99" fill="none" stroke={ink} strokeWidth="1.9" strokeLinejoin="round" />
      <g transform="translate(24.56 28.6) scale(0.46)">
        <path
          d="M 26 4 C 14 4 14 10 14 20 C 14 30 10 36 2 36 C 10 36 14 42 14 52 C 14 62 14 68 26 68"
          fill="none"
          stroke={accent}
          strokeWidth="8.7"
          strokeLinecap="round"
        />
      </g>
      <g transform="translate(62.56 28.6) scale(0.46)">
        <path
          d="M 2 4 C 14 4 14 10 14 20 C 14 30 18 36 26 36 C 18 36 14 42 14 52 C 14 62 14 68 2 68"
          fill="none"
          stroke={accent}
          strokeWidth="8.7"
          strokeLinecap="round"
        />
      </g>
    </svg>
  );
}
