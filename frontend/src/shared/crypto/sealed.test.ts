import { describe, expect, it } from 'vitest';

import { fromBase64Url, toBase64Url } from './base64url.ts';
import { utf8 } from './bytes.ts';
import { deriveKeyBits, itemAad, itemKey, metaAad, packageSalt } from './keys.ts';
import { isSealed, open, seal, SEALED_OVERHEAD_BYTES } from './sealed.ts';

const PACKAGE_ID = '0192f3a0-1111-7000-8000-000000000000';
const OTHER_PACKAGE_ID = '0192f3a0-2222-7000-8000-000000000000';
const ITEM_ID = '0192f3a0-3333-7000-8000-000000000000';
const OTHER_ITEM_ID = '0192f3a0-4444-7000-8000-000000000000';

const createContentKey = async (itemId = ITEM_ID, packageId = PACKAGE_ID) => {
  const packageKey = await deriveKeyBits('correct horse battery staple', await packageSalt(packageId));
  return itemKey(packageKey, itemId);
};

describe('seal and open', () => {
  it('round-trips arbitrary bytes with the same key and AAD', async () => {
    const key = await createContentKey();
    const plain = crypto.getRandomValues(new Uint8Array(1024));

    const sealed = await seal(key, plain, itemAad(PACKAGE_ID, ITEM_ID));
    const opened = await open(key, sealed, itemAad(PACKAGE_ID, ITEM_ID));

    expect(opened).toEqual(plain);
  });

  it('round-trips text bytes too — there is no per-type branch', async () => {
    const key = await createContentKey();
    const plain = utf8('Grüße 世界, one envelope');

    const sealed = await seal(key, plain, itemAad(PACKAGE_ID, ITEM_ID));

    expect(new TextDecoder().decode(await open(key, sealed, itemAad(PACKAGE_ID, ITEM_ID)))).toBe(
      'Grüße 世界, one envelope'
    );
  });

  it('writes the SIE2 magic and fixed overhead', async () => {
    const key = await createContentKey();
    const plain = utf8('small');
    const sealed = await seal(key, plain, itemAad(PACKAGE_ID, ITEM_ID));

    expect(isSealed(sealed)).toBe(true);
    expect(sealed.length).toBe(plain.length + SEALED_OVERHEAD_BYTES);
    expect(toBase64Url(sealed.slice(0, 4))).toBe('U0lFMg');
  });

  it('rejects tampered ciphertext', async () => {
    const key = await createContentKey();
    const sealed = await seal(key, utf8('secret'), itemAad(PACKAGE_ID, ITEM_ID));
    sealed[sealed.length - 1] ^= 0xff;

    await expect(open(key, sealed, itemAad(PACKAGE_ID, ITEM_ID))).rejects.toThrow();
  });

  it('rejects a blob sealed for a different package', async () => {
    const key = await createContentKey();
    const sealed = await seal(key, utf8('secret'), itemAad(PACKAGE_ID, ITEM_ID));

    await expect(open(key, sealed, itemAad(OTHER_PACKAGE_ID, ITEM_ID))).rejects.toThrow();
  });

  it('rejects a blob sealed for a different item', async () => {
    const key = await createContentKey();
    const sealed = await seal(key, utf8('secret'), itemAad(PACKAGE_ID, ITEM_ID));

    await expect(open(key, sealed, itemAad(PACKAGE_ID, OTHER_ITEM_ID))).rejects.toThrow();
  });

  it('rejects a content blob presented as metadata', async () => {
    const key = await createContentKey();
    const sealed = await seal(key, utf8('secret'), itemAad(PACKAGE_ID, ITEM_ID));

    await expect(open(key, sealed, metaAad(PACKAGE_ID, ITEM_ID))).rejects.toThrow();
  });

  it('treats raw bytes (and an empty array) as not sealed', () => {
    expect(isSealed(new Uint8Array())).toBe(false);
    expect(isSealed(utf8('plain text'))).toBe(false);
    expect(isSealed(fromBase64Url('aGVsbG8'))).toBe(false);
  });

  it('refuses to open bytes without the magic', async () => {
    const key = await createContentKey();

    await expect(open(key, utf8('not sealed'), itemAad(PACKAGE_ID, ITEM_ID))).rejects.toThrow('Payload is not sealed');
  });
});
