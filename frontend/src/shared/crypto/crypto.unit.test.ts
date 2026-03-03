import { describe, expect, it } from 'vitest';

import { parseEncryptedTextEnvelope } from './envelope';
import { decryptText, encryptText, generatePassphrase } from './crypto';

describe('generatePassphrase', () => {
  it('returns a URL-safe base64 passphrase', () => {
    const passphrase = generatePassphrase();

    expect(passphrase).toHaveLength(22);
    expect(passphrase).toMatch(/^[A-Za-z0-9_-]+$/);
  });
});

describe('encryptText and decryptText', () => {
  it('encrypts and decrypts text with the same passphrase', async () => {
    const plainText = 'Hello ShareIt 123';
    const passphrase = 'test-passphrase';

    const payload = await encryptText(plainText, passphrase);
    const decrypted = await decryptText(payload, passphrase);

    expect(payload).not.toContain(plainText);
    expect(decrypted).toBe(plainText);
  });

  it('supports unicode text roundtrip', async () => {
    const plainText = 'Grüße 世界';
    const passphrase = 'unicode-passphrase';

    const payload = await encryptText(plainText, passphrase);
    const decrypted = await decryptText(payload, passphrase);

    expect(decrypted).toBe(plainText);
  });

  it('creates a parseable encrypted envelope', async () => {
    const payload = await encryptText('content', 'passphrase');
    const envelope = parseEncryptedTextEnvelope(payload);

    expect(envelope).not.toBeNull();
    expect(envelope?.v).toBe(1);
    expect(envelope?.alg).toBe('AES-GCM');
    expect(envelope?.iter).toBeGreaterThan(0);
    expect(envelope?.salt).toMatch(/^[A-Za-z0-9_-]+$/);
    expect(envelope?.iv).toMatch(/^[A-Za-z0-9_-]+$/);
    expect(envelope?.ct).toMatch(/^[A-Za-z0-9_-]+$/);
  });

  it('throws when decrypting a non-encrypted payload', async () => {
    await expect(decryptText('not-json', 'passphrase')).rejects.toThrow('Payload is not encrypted');
  });

  it('rejects when passphrase is wrong', async () => {
    const payload = await encryptText('secret', 'correct-passphrase');

    await expect(decryptText(payload, 'wrong-passphrase')).rejects.toThrow();
  });
});
