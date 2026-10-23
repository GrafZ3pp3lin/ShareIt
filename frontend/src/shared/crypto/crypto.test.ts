import { describe, expect, it } from 'vitest';

import { parseEncryptedTextEnvelope } from './envelope.ts';
import {
  decryptFile,
  decryptText,
  encryptFile,
  encryptText,
  generatePassphrase,
  isEncryptedBlob,
  isEncryptedFileEnvelope,
  ENCRYPTED_FILE_MIME_TYPE,
  ENCRYPTED_FILE_OVERHEAD_BYTES
} from './crypto.ts';

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

describe('encryptFile and decryptFile', () => {
  const makeFile = (bytes: Uint8Array<ArrayBuffer>, name = 'photo.jpg', type = 'image/jpeg') =>
    new File([bytes], name, { type });

  it('roundtrips file bytes with the same passphrase', async () => {
    const plain = crypto.getRandomValues(new Uint8Array(1024));
    const passphrase = 'test-passphrase';

    const encrypted = await encryptFile(makeFile(plain), passphrase);
    expect(encrypted.name).toBe('photo.jpg');
    expect(encrypted.type).toBe(ENCRYPTED_FILE_MIME_TYPE);
    expect(encrypted.size).toBe(plain.length + ENCRYPTED_FILE_OVERHEAD_BYTES);

    const decrypted = await decryptFile(encrypted, passphrase);
    expect(new Uint8Array(await decrypted.arrayBuffer())).toEqual(plain);
  });

  it('isEncryptedFileEnvelope detects the magic header', async () => {
    const encrypted = await encryptFile(makeFile(new Uint8Array([1, 2, 3]), 'a.bin'), 'passphrase');
    const bytes = new Uint8Array(await encrypted.arrayBuffer());

    expect(isEncryptedFileEnvelope(bytes)).toBe(true);
    expect(await isEncryptedBlob(encrypted)).toBe(true);
    expect(await isEncryptedBlob(makeFile(new Uint8Array([1, 2, 3]), 'b.bin'))).toBe(false);
  });

  it('rejects when the passphrase is wrong', async () => {
    const encrypted = await encryptFile(makeFile(new Uint8Array([1, 2, 3]), 'a.bin'), 'correct');
    await expect(decryptFile(encrypted, 'wrong')).rejects.toThrow();
  });

  it('rejects a plain (non-enveloped) blob', async () => {
    await expect(decryptFile(makeFile(new Uint8Array([1, 2, 3]), 'a.bin'), 'passphrase')).rejects.toThrow(
      'Payload is not encrypted'
    );
  });
});
