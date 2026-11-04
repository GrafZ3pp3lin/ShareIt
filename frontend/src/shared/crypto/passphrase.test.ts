import { describe, expect, it } from 'vitest';

import { generatePassphrase, validatePassphrase } from './passphrase.ts';

describe('generatePassphrase', () => {
  it('returns a URL-safe base64 passphrase', () => {
    const passphrase = generatePassphrase();

    expect(passphrase).toHaveLength(22);
    expect(passphrase).toMatch(/^[A-Za-z0-9_-]+$/);
  });
});

describe('validatePassphrase', () => {
  it('rejects values shorter than 12 characters', () => {
    expect(validatePassphrase('short')).toEqual({ valid: false, strength: 0 });
    expect(validatePassphrase('12345678901')).toEqual({ valid: false, strength: 0 });
  });

  it('accepts 12 characters as a weak passphrase', () => {
    expect(validatePassphrase('abcdefghijkl')).toEqual({ valid: true, strength: 1 });
  });

  it('raises the strength for longer and more varied passphrases', () => {
    expect(validatePassphrase('correct horse battery staple')).toEqual({ valid: true, strength: 3 });
  });
});
