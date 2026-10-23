import { describe, expect, it } from 'vitest';

import { parseEncryptedTextEnvelope } from './envelope.ts';

describe('parseEncryptedTextEnvelope', () => {
  it('parses a valid envelope', () => {
    const value = JSON.stringify({
      v: 1,
      alg: 'AES-GCM',
      iter: 100000,
      salt: 'salt',
      iv: 'iv',
      ct: 'ciphertext'
    });

    expect(parseEncryptedTextEnvelope(value)).toEqual({
      v: 1,
      alg: 'AES-GCM',
      iter: 100000,
      salt: 'salt',
      iv: 'iv',
      ct: 'ciphertext'
    });
  });

  it('returns null for invalid JSON', () => {
    expect(parseEncryptedTextEnvelope('not-json')).toBeNull();
  });

  it('returns null when required fields are missing or invalid', () => {
    expect(
      parseEncryptedTextEnvelope(
        JSON.stringify({
          v: 1,
          alg: 'AES-GCM',
          iter: '100000',
          salt: 'salt',
          iv: 'iv',
          ct: 'ciphertext'
        })
      )
    ).toBeNull();

    expect(
      parseEncryptedTextEnvelope(
        JSON.stringify({
          v: 2,
          alg: 'AES-GCM',
          iter: 100000,
          salt: 'salt',
          iv: 'iv',
          ct: 'ciphertext'
        })
      )
    ).toBeNull();
  });
});
