import { createContext, createEffect, createSignal, useContext } from 'solid-js';
import { THEME_STORAGE_KEY, resolveInitialTheme, type Theme } from './theme.ts';

type ThemeContextValue = {
  // Solid 2.0: a context value is snapshotted once by its provider, so the
  // reactive mode is carried as an accessor.
  mode: () => Theme;
  toggle: () => void;
};

const ThemeContext = createContext<ThemeContextValue>();

export function ThemeProvider(props: { children: any }) {
  const initial = resolveInitialTheme(
    localStorage.getItem(THEME_STORAGE_KEY),
    window.matchMedia('(prefers-color-scheme: dark)').matches
  );
  const [mode, setMode] = createSignal<Theme>(initial);

  createEffect(
    () => mode(),
    (current) => {
      document.documentElement.classList.toggle('dark', current === 'dark');
      localStorage.setItem(THEME_STORAGE_KEY, current);
    }
  );

  const value: ThemeContextValue = {
    mode,
    toggle: () => setMode((current) => (current === 'dark' ? 'light' : 'dark'))
  };

  return <ThemeContext value={value}>{props.children}</ThemeContext>;
}

export function useTheme() {
  return useContext(ThemeContext);
}
