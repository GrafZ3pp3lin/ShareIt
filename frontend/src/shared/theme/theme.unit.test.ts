import { describe, expect, it } from 'vitest';
import { resolveInitialTheme } from './theme.ts';

describe('resolveInitialTheme', () => {
  it('uses the stored value when present', () => {
    expect(resolveInitialTheme('dark', false)).toBe('dark');
    expect(resolveInitialTheme('light', true)).toBe('light');
  });

  it('falls back to the system preference when nothing is stored', () => {
    expect(resolveInitialTheme(null, true)).toBe('dark');
    expect(resolveInitialTheme(null, false)).toBe('light');
  });

  it('ignores an invalid stored value and falls back to the system preference', () => {
    expect(resolveInitialTheme('invalid', true)).toBe('dark');
    expect(resolveInitialTheme('invalid', false)).toBe('light');
  });
});
