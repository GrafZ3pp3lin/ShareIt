import { afterEach, describe, expect, it, vi } from 'vitest';
import { getGuestId } from './guest.ts';

const memoryStorage = () => {
  const values = new Map<string, string>();
  return {
    values,
    getItem: (key: string) => values.get(key) ?? null,
    setItem: (key: string, value: string) => {
      values.set(key, value);
    }
  };
};

describe('getGuestId', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('returns undefined outside a browser', () => {
    vi.stubGlobal('window', undefined);

    expect(getGuestId()).toBeUndefined();
  });

  it('creates and persists a guest id on first use', () => {
    const storage = memoryStorage();
    vi.stubGlobal('window', { localStorage: storage });

    const guestId = getGuestId();

    expect(guestId).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/);
    expect(storage.values.get('shareit-guest-id')).toBe(guestId);
  });

  it('reuses the persisted guest id', () => {
    const storage = memoryStorage();
    storage.values.set('shareit-guest-id', 'existing-guest');
    vi.stubGlobal('window', { localStorage: storage });

    expect(getGuestId()).toBe('existing-guest');
  });
});
