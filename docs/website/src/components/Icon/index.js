import React from 'react';

// The few Lucide icons the site draws (lucide-static 0.460.0, ISC license, https://lucide.dev), as the design
// system's Icon component draws them: a 24-unit line icon, stroke 2, round caps, tinted by currentColor.
const icons = {
  'arrow-right': ['M5 12h14', 'm12 5 7 7-7 7'],
  contrast: ['circle:12,12,10', 'M12 18a6 6 0 0 0 0-12v12z'],
  github: [
    'M15 22v-4a4.8 4.8 0 0 0-1-3.5c3 0 6-2 6-5.5.08-1.25-.27-2.48-1-3.5.28-1.15.28-2.35 0-3.5 0 0-1 0-3 1.5-2.64-.5-5.36-.5-8 0C6 2 5 2 5 2c-.3 1.15-.3 2.35 0 3.5A5.403 5.403 0 0 0 4 9c0 3.5 3 5.5 6 5.5-.39.49-.68 1.05-.85 1.65-.17.6-.22 1.23-.15 1.85v4',
    'M9 18c-4.51 2-5-2-7-2',
  ],
  moon: ['M12 3a6 6 0 0 0 9 9 9 9 0 1 1-9-9Z'],
  search: ['circle:11,11,8', 'm21 21-4.3-4.3'],
  sun: [
    'circle:12,12,4',
    'M12 2v2',
    'M12 20v2',
    'm4.93 4.93 1.41 1.41',
    'm17.66 17.66 1.41 1.41',
    'M2 12h2',
    'M20 12h2',
    'm6.34 17.66-1.41 1.41',
    'm19.07 4.93-1.41 1.41',
  ],
};

/** @param {{name: keyof typeof icons, size?: number, className?: string}} props */
export default function Icon({ name, size = 18, className }) {
  return (
    <svg
      xmlns="http://www.w3.org/2000/svg"
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      className={className}
      style={{ flex: 'none' }}>
      {icons[name].map((shape) => {
        if (shape.startsWith('circle:')) {
          const [cx, cy, r] = shape.slice('circle:'.length).split(',');
          return <circle key={shape} cx={cx} cy={cy} r={r} />;
        }
        return <path key={shape} d={shape} />;
      })}
    </svg>
  );
}
