import type { ReactNode } from 'react';
import type { ResourceKind } from '../api';

/** Simple line icons drawn for PyroLens (24×24 grid, stroke in the current text colour). */
const ICONS = {
  flame: (
    <>
      <path d="M12 3c.8 3.2 4.5 5 4.5 9.5a4.5 4.5 0 0 1-9 0c0-2 .8-3.4 2-4.5.3 1.6 1 2.5 2 2.8C11.3 8.4 11.2 5.6 12 3z" />
      <path d="M12 20.5c-1.4 0-2.3-1-2.3-2.3 0-1.2.8-2 1.6-2.7.1.8.5 1.2 1 1.3.2-1 .3-1.8.1-2.8 1.3.8 2 2.2 2 3.9 0 1.5-1 2.6-2.4 2.6z" />
    </>
  ),
  sun: (
    <>
      <circle cx="12" cy="12" r="4" />
      <path d="M12 2.5v2M12 19.5v2M2.5 12h2M19.5 12h2M5.3 5.3l1.4 1.4M17.3 17.3l1.4 1.4M5.3 18.7l1.4-1.4M17.3 6.7l1.4-1.4" />
    </>
  ),
  moon: <path d="M20 14.5A8 8 0 0 1 9.5 4a8 8 0 1 0 10.5 10.5z" />,
  lock: (
    <>
      <rect x="5" y="11" width="14" height="9.5" rx="2" />
      <path d="M8 11V8a4 4 0 0 1 8 0v3" />
    </>
  ),
  unlock: (
    <>
      <rect x="5" y="11" width="14" height="9.5" rx="2" />
      <path d="M8 11V8a4 4 0 0 1 7.6-1.8" />
    </>
  ),
  back: <path d="M19 12H5M11 6l-6 6 6 6" />,
  plane: <path d="M12 2.5v19M3.5 13.5 12 10l8.5 3.5M8.5 20.5 12 19l3.5 1.5" />,
  helicopter: (
    <>
      <path d="M4 5h16M12 5v3.5M17 13.5h4M21 11.5v4M9.5 17l-1 3h7l-1-3" />
      <ellipse cx="12" cy="13.5" rx="5" ry="3.5" />
    </>
  ),
  drone: (
    <>
      <path d="M8 8l8 8M16 8l-8 8" />
      <circle cx="6" cy="6" r="2.5" />
      <circle cx="18" cy="6" r="2.5" />
      <circle cx="6" cy="18" r="2.5" />
      <circle cx="18" cy="18" r="2.5" />
    </>
  ),
  engine: (
    <>
      <path d="M2.5 7h10.5v9.5H2.5zM13 10h4l3.5 3.5v3H13" />
      <circle cx="6.5" cy="18" r="1.8" />
      <circle cx="16.5" cy="18" r="1.8" />
    </>
  ),
  crew: (
    <>
      <circle cx="9" cy="8" r="3" />
      <path d="M3.5 20a5.5 5.5 0 0 1 11 0" />
      <circle cx="17" cy="9.5" r="2.3" />
      <path d="M15.2 14.7A4.5 4.5 0 0 1 21 19" />
    </>
  ),
} satisfies Record<string, ReactNode>;

export type IconName = keyof typeof ICONS;

const KIND_ICON: Record<ResourceKind, IconName> = {
  aircraft: 'plane',
  helicopter: 'helicopter',
  drone: 'drone',
  engine: 'engine',
  crew: 'crew',
};

export function Icon({ name, size = 18, label }: { name: IconName; size?: number; label?: string }) {
  return (
    <svg
      className="icon"
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.7}
      strokeLinecap="round"
      strokeLinejoin="round"
      role={label ? 'img' : undefined}
      aria-label={label}
      aria-hidden={label ? undefined : true}
    >
      {ICONS[name]}
    </svg>
  );
}

export const KindIcon = ({ kind }: { kind: ResourceKind }) => <Icon name={KIND_ICON[kind]} size={20} />;
