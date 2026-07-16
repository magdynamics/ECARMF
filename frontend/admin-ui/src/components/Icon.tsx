// Design System 2.0 icon set: hand-rolled 24×24 stroke icons (no dependency),
// replacing the emoji that made the product feel unfinished. All icons share
// one visual grammar — 1.8px rounded strokes, currentColor — so they inherit
// text color and scale with font size context.

const PATHS: Record<string, string> = {
  home: 'M3 10.5 12 3l9 7.5M5 9.5V21h5v-6h4v6h5V9.5',
  map: 'M9 4 3 6v14l6-2 6 2 6-2V4l-6 2-6-2zM9 4v14M15 6v14',
  book: 'M4 5a2 2 0 0 1 2-2h14v18H6a2 2 0 0 0-2 2zM4 19a2 2 0 0 1 2-2h14M8 7h8',
  compass: 'M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18zM15 9l-2 5-4 1 2-5 4-1z',
  building: 'M4 21V4a1 1 0 0 1 1-1h9a1 1 0 0 1 1 1v17M15 9h4a1 1 0 0 1 1 1v11M2 21h20M8 7h3M8 11h3M8 15h3',
  package: 'M12 3l8 4.5v9L12 21l-8-4.5v-9L12 3zM4 7.5l8 4.5 8-4.5M12 12v9',
  shield: 'M12 3l7 3v6c0 4.4-3 7.5-7 9-4-1.5-7-4.6-7-9V6l7-3zM9 12l2 2 4-4',
  plug: 'M9 3v5M15 3v5M6 8h12v3a6 6 0 0 1-5 5.9V21h-2v-4.1A6 6 0 0 1 6 11V8z',
  target: 'M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18zM12 16.5a4.5 4.5 0 1 0 0-9 4.5 4.5 0 0 0 0 9zM12 13a1 1 0 1 0 0-2 1 1 0 0 0 0 2z',
  calendar: 'M5 5h14a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1zM4 10h16M8 3v4M16 3v4',
  landmark: 'M3 9l9-5 9 5v1H3V9zM5 10v8M9.5 10v8M14.5 10v8M19 10v8M3 20h18',
  cpu: 'M8 8h8v8H8V8zM5 5h14v14H5V5zM9 2v3M15 2v3M9 19v3M15 19v3M2 9h3M2 15h3M19 9h3M19 15h3',
  'inbox-in': 'M12 3v9m0 0-3-3m3 3 3-3M4 13v5a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-5M4 15h4l2 2h4l2-2h4',
  'file-search': 'M14 3H6a1 1 0 0 0-1 1v16a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1V8l-5-5zM14 3v5h5M11 15.5a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5zM13 15l2.5 2.5',
  list: 'M8 6h13M8 12h13M8 18h13M4 6h.01M4 12h.01M4 18h.01',
  gauge: 'M5 19a9 9 0 1 1 14 0M12 13l3.5-3.5M12 15a2 2 0 1 0 0-4 2 2 0 0 0 0 4z',
  'alert-triangle': 'M12 4 2.5 20h19L12 4zM12 10v4M12 17.5h.01',
  wrench: 'M14 7a4 4 0 0 1 5.5-3.7L16 6.8l1.2 1.2L20.7 4.5A4 4 0 0 1 17 10c-.5 0-1-.1-1.4-.3L7 18.3A2 2 0 1 1 4.2 15.5L12.8 7c-.2-.4-.3-.9-.3-1.4',
  'trend-up': 'M3 17l6-6 4 4 8-8M15 7h6v6',
  folders: 'M7 7V5a1 1 0 0 1 1-1h3l2 2h6a1 1 0 0 1 1 1v2M4 8h4l2 2h9a1 1 0 0 1 1 1v8a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V9a1 1 0 0 1 1-1z',
  'file-text': 'M14 3H6a1 1 0 0 0-1 1v16a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1V8l-5-5zM14 3v5h5M9 12h6M9 16h6',
  'book-open': 'M12 6c-1.5-1.6-3.8-2-6-2-1 0-2 .1-3 .4V19c1-.3 2-.4 3-.4 2.2 0 4.5.4 6 2 1.5-1.6 3.8-2 6-2 1 0 2 .1 3 .4V4.4c-1-.3-2-.4-3-.4-2.2 0-4.5.4-6 2v14.6',
  signal: 'M4 19v-4M9 19v-8M14 19V8M19 19V4',
  'clipboard-check': 'M9 4h6v3H9V4zM15 4h3a1 1 0 0 1 1 1v15a1 1 0 0 1-1 1H6a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1h3M9 13.5l2 2 4-4',
  archive: 'M3 5h18v4H3V5zM5 9v10a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1V9M10 13h4',
  briefcase: 'M4 8h16a1 1 0 0 1 1 1v10a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V9a1 1 0 0 1 1-1zM9 8V6a2 2 0 0 1 2-2h2a2 2 0 0 1 2 2v2M3 13h18',
  bot: 'M8 8h8a3 3 0 0 1 3 3v5a3 3 0 0 1-3 3H8a3 3 0 0 1-3-3v-5a3 3 0 0 1 3-3zM12 8V4M12 4h.01M9.5 13.5h.01M14.5 13.5h.01M2 13v3M22 13v3',
  sparkles: 'M12 4l1.8 4.2L18 10l-4.2 1.8L12 16l-1.8-4.2L6 10l4.2-1.8L12 4zM19 15l.9 2.1L22 18l-2.1.9L19 21l-.9-2.1L16 18l2.1-.9L19 15zM5 3l.7 1.6L7.3 5.3 5.7 6 5 7.6 4.3 6 2.7 5.3 4.3 4.6 5 3z',
  inbox: 'M4 4h16a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1zM3 13h5l2 3h4l2-3h5',
  'alert-octagon': 'M8.2 3h7.6L21 8.2v7.6L15.8 21H8.2L3 15.8V8.2L8.2 3zM12 8v4.5M12 16h.01',
  activity: 'M3 12h4l3-8 4 16 3-8h4',
  'user-plus': 'M10 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8zM3 21a7 7 0 0 1 13.3-3M18 9v6M15 12h6',
  library: 'M4 20V5M8 20V5M12 20V6l6-1.5L21.5 19 15 20.5',
  puzzle: 'M10 4a2 2 0 1 1 4 0h4a1 1 0 0 1 1 1v4a2 2 0 1 1 0 4v4a1 1 0 0 1-1 1h-4a2 2 0 1 0-4 0H6a1 1 0 0 1-1-1v-4a2 2 0 1 0 0-4V5a1 1 0 0 1 1-1h4z',
  grid: 'M4 4h7v7H4V4zM13 4h7v7h-7V4zM4 13h7v7H4v-7zM13 13h7v7h-7v-7z',
  play: 'M6 4.5v15l13-7.5-13-7.5z',
  users: 'M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8zM2 21a7 7 0 0 1 14 0M16 3.5a4 4 0 0 1 0 7M18 14a7 7 0 0 1 4 6.2',
  'credit-card': 'M3 6h18a1 1 0 0 1 1 1v10a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1zM2 10h20M6 15h4',
  mail: 'M4 5h16a1 1 0 0 1 1 1v12a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1zM3 7l9 6 9-6',
  search: 'M11 18a7 7 0 1 0 0-14 7 7 0 0 0 0 14zM16 16l5 5',
  menu: 'M4 6h16M4 12h16M4 18h16',
  x: 'M6 6l12 12M18 6 6 18',
  check: 'M4.5 12.5l5 5 10-11',
  'alert-circle': 'M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18zM12 8v4.5M12 16h.01',
  'chevron-down': 'M6 9l6 6 6-6',
  'chevron-right': 'M9 6l6 6-6 6',
  sun: 'M12 16.5a4.5 4.5 0 1 0 0-9 4.5 4.5 0 0 0 0 9zM12 2v2.5M12 19.5V22M2 12h2.5M19.5 12H22M4.9 4.9l1.8 1.8M17.3 17.3l1.8 1.8M4.9 19.1l1.8-1.8M17.3 6.7l1.8-1.8',
  moon: 'M20 14.5A8 8 0 0 1 9.5 4 8 8 0 1 0 20 14.5z',
  plus: 'M12 5v14M5 12h14',
  'arrow-right': 'M4 12h16m0 0-6-6m6 6-6 6',
  key: 'M14 11a5 5 0 1 0-4.5 4.98L11 14.5h2v-2h2l1-1.5h-2z M8.5 15.5 3 21M5.5 18.5 7 20',
  lightbulb: 'M9 21h6M10 18h4M12 3a6 6 0 0 1 3.5 10.9c-.9.7-1.5 1.3-1.5 2.1h-4c0-.8-.6-1.4-1.5-2.1A6 6 0 0 1 12 3z',
  'thumbs-up': 'M7 11v10H3V11h4zM7 11l4-8a2 2 0 0 1 2 2v4h6a2 2 0 0 1 2 2.3l-1.2 7A2 2 0 0 1 17.8 20H7',
  'thumbs-down': 'M7 13V3H3v10h4zM7 13l4 8a2 2 0 0 0 2-2v-4h6a2 2 0 0 0 2-2.3l-1.2-7A2 2 0 0 0 17.8 4H7',
  paperclip: 'M20 11.5 12 19.5a5 5 0 0 1-7-7l8.5-8.5a3.5 3.5 0 0 1 5 5L10 17.5a2 2 0 0 1-3-3L14.5 7',
  image: 'M4 4h16a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1zM8.5 10.5a1.5 1.5 0 1 0 0-3 1.5 1.5 0 0 0 0 3zM21 15l-5-5-8 8',
}

export type IconName = keyof typeof PATHS

export function Icon({ name, size = 17, className }: { name: string; size?: number; className?: string }) {
  const d = PATHS[name]
  if (!d) return null
  return (
    <svg
      className={className}
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.8"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <path d={d} />
    </svg>
  )
}
