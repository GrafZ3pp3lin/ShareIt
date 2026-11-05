import { fireEvent, render, screen } from '@solidjs/testing-library';
import { flush } from 'solid-js';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { THEME_STORAGE_KEY } from './theme.ts';
import { ThemeProvider, useTheme } from './theme-context.tsx';

const Probe = () => {
  const theme = useTheme();
  return (
    <button type="button" onClick={theme.toggle}>
      {theme.mode()}
    </button>
  );
};

const renderTheme = () =>
  render(() => (
    <ThemeProvider>
      <Probe />
    </ThemeProvider>
  ));

describe('ThemeProvider', () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.classList.remove('dark');
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('applies the default light theme and persists it', () => {
    renderTheme();

    expect(screen.getByRole('button', { name: 'light' })).toBeInTheDocument();
    expect(document.documentElement.classList.contains('dark')).toBe(false);
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('light');
  });

  it('starts from the stored theme', () => {
    localStorage.setItem(THEME_STORAGE_KEY, 'dark');

    renderTheme();

    expect(screen.getByRole('button', { name: 'dark' })).toBeInTheDocument();
    expect(document.documentElement.classList.contains('dark')).toBe(true);
  });

  it('toggles the document class and storage', () => {
    renderTheme();

    fireEvent.click(screen.getByRole('button', { name: 'light' }));
    flush();

    expect(screen.getByRole('button', { name: 'dark' })).toBeInTheDocument();
    expect(document.documentElement.classList.contains('dark')).toBe(true);
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('dark');

    fireEvent.click(screen.getByRole('button', { name: 'dark' }));
    flush();

    expect(screen.getByRole('button', { name: 'light' })).toBeInTheDocument();
    expect(document.documentElement.classList.contains('dark')).toBe(false);
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('light');
  });
});
