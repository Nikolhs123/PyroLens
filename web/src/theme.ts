export type Theme = 'light' | 'dark';

const STORAGE_KEY = 'pyrolens.theme';

/** The saved choice, else the operating system's preference. index.html applies it before the first paint. */
export function initialTheme(): Theme {
  try {
    const saved = localStorage.getItem(STORAGE_KEY);
    if (saved === 'light' || saved === 'dark') return saved;
  } catch {
    // Storage blocked: fall back to the system setting.
  }
  return window.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

export function applyTheme(theme: Theme): void {
  document.documentElement.dataset.theme = theme;
  try {
    localStorage.setItem(STORAGE_KEY, theme);
  } catch {
    // Not remembered; still applied for this visit.
  }
}
