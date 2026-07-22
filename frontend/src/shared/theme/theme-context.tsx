import { createContext, createEffect, createMemo, createSignal, useContext } from 'solid-js';
import { THEME_STORAGE_KEY, resolveInitialTheme, type Theme } from './theme.ts';

type ThemeContextValue = {
  mode: Theme;
  toggle: () => void;
};

const ThemeContext = createContext<ThemeContextValue>();

export function ThemeProvider(props: { children: any }) {
  const initial = resolveInitialTheme(
    localStorage.getItem(THEME_STORAGE_KEY),
    window.matchMedia('(prefers-color-scheme: dark)').matches
  );
  const [mode, setMode] = createSignal<Theme>(initial);

  createEffect(() => {
    document.documentElement.classList.toggle('dark', mode() === 'dark');
    localStorage.setItem(THEME_STORAGE_KEY, mode());
  });

  const value = createMemo<ThemeContextValue>(() => ({
    mode: mode(),
    toggle: () => setMode((current) => (current === 'dark' ? 'light' : 'dark'))
  }));

  return <ThemeContext.Provider value={value()}>{props.children}</ThemeContext.Provider>;
}

export function useTheme() {
  const ctx = useContext(ThemeContext);
  if (!ctx) {
    throw new Error('ThemeContext is not available');
  }
  return ctx;
}
