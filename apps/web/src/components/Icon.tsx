// ponytail: a handful of inline stroke icons; switch to an icon package only if the set grows a lot.
const paths = {
  sparkles: 'm12 3 2.3 6.7L21 12l-6.7 2.3L12 21l-2.3-6.7L3 12l6.7-2.3zM20 2v4M18 4h4',
  loans: 'M9 3h6v3H9zM7 4.5H5V21h14V4.5h-2M9 11h6M9 15h4',
  catalog: 'M3.5 3.5h7l10 10-7 7-10-10zM7.5 7.5h.01',
  inventory: 'M3 7.5 12 3l9 4.5-9 4.5zM3 7.5v9L12 21l9-4.5v-9M12 12v9',
  users: 'M16 21v-2a4 4 0 0 0-8 0v2M12 11a3.5 3.5 0 1 0 0-7 3.5 3.5 0 0 0 0 7M21 21v-2a4 4 0 0 0-3-3.9M3 21v-2a4 4 0 0 1 3-3.9',
  home: 'M3 11 12 4l9 7M5.5 9.5V20h13V9.5M10 20v-5h4v5',
  equipment: 'M9 3h6M10 3v6l-5.5 9.5A2 2 0 0 0 6.2 21h11.6a2 2 0 0 0 1.7-2.5L14 9V3M7.5 15h9',
  bell: 'M6 16v-5a6 6 0 0 1 12 0v5l2 2H4zM10 20.5a2 2 0 0 0 4 0',
  user: 'M20 21a8 8 0 0 0-16 0M12 13a4.5 4.5 0 1 0 0-9 4.5 4.5 0 0 0 0 9',
  copy: 'M9 9h11v11H9zM5 15H4V4h11v1',
  check: 'm5 12.5 4.5 4.5L19 7.5',
  sun: 'M12 7.5a4.5 4.5 0 1 0 0 9 4.5 4.5 0 0 0 0-9M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4',
  moon: 'M20.5 14A8.5 8.5 0 1 1 10 3.5a6.5 6.5 0 0 0 10.5 10.5',
  search: 'M11 4a7 7 0 1 0 0 14 7 7 0 0 0 0-14M20.5 20.5 16 16',
  plus: 'M12 5v14M5 12h14',
  minus: 'M5 12h14',
  logout: 'M15 4h4v16h-4M10 8l-4 4 4 4M6 12h10',
} as const;
export type IconName = keyof typeof paths;

export function Icon({ name, size = 18 }: { name: IconName; size?: number }) {
  return <svg className="icon" viewBox="0 0 24 24" width={size} height={size} fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" focusable="false"><path d={paths[name]} /></svg>;
}
