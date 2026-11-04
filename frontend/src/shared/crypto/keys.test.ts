import { describe, expect, it } from 'vitest';

import { toBase64Url } from './base64url.ts';
import { normalizeId } from './ids.ts';
import { deriveKeyBits, itemAad, metaAad, metaKey, itemKey, packageSalt, unlockProof } from './keys.ts';

const PACKAGE_ID = '0192f3a0-1111-7000-8000-000000000000';
const OTHER_PACKAGE_ID = '0192f3a0-2222-7000-8000-000000000000';
const ITEM_ID = '0192f3a0-3333-7000-8000-000000000000';
const PASSPHRASE = 'correct horse battery staple';

const derivePackageKey = async (passphrase = PASSPHRASE, packageId = PACKAGE_ID) =>
  deriveKeyBits(passphrase, await packageSalt(packageId));

describe('normalizeId', () => {
  it('formats any UUID spelling into the lowercase hyphenated form', () => {
    expect(normalizeId(PACKAGE_ID.toUpperCase())).toBe(PACKAGE_ID);
    expect(normalizeId(PACKAGE_ID.replace(/-/g, ''))).toBe(PACKAGE_ID);
  });

  it('rejects values that are not UUIDs', () => {
    expect(() => normalizeId('package-1')).toThrow('Invalid UUID');
  });
});

describe('packageSalt', () => {
  it('matches the documented vector', async () => {
    expect(toBase64Url(await packageSalt(PACKAGE_ID))).toBe('12Uai6r9WV5EvNkGnWkStlVkvaj5cTnkn8RfjyKFiq4');
  });

  it('is stable for differently formatted ids and unique per package', async () => {
    const canonical = toBase64Url(await packageSalt(PACKAGE_ID));
    const reformatted = toBase64Url(await packageSalt(PACKAGE_ID.replace(/-/g, '').toUpperCase()));
    const other = toBase64Url(await packageSalt(OTHER_PACKAGE_ID));

    expect(reformatted).toBe(canonical);
    expect(other).not.toBe(canonical);
  });
});

describe('unlockProof', () => {
  it('matches the documented vector', async () => {
    const key = await derivePackageKey();

    expect(await unlockProof(key, PACKAGE_ID)).toBe('oRKAwBzZ0We94hihlU6GaJYfmlo8bd5C8fwLbO9pwy8');
  });

  it('matches the documented vector for a second package', async () => {
    const key = await derivePackageKey(PASSPHRASE, OTHER_PACKAGE_ID);

    expect(await unlockProof(key, OTHER_PACKAGE_ID)).toBe('JT4GW4fBuWJvs_1AF8opfPOHW3ONCNxHxcvNnz4bWbE');
  });

  it('changes when the same key is used with another package id', async () => {
    const key = await derivePackageKey();

    expect(await unlockProof(key, OTHER_PACKAGE_ID)).not.toBe(await unlockProof(key, PACKAGE_ID));
  });
});

describe('AAD helpers', () => {
  it('bind the normalized package and item with distinct role prefixes', () => {
    const decoder = new TextDecoder();

    expect(decoder.decode(itemAad(PACKAGE_ID, ITEM_ID))).toBe(`shareit/item:1:${PACKAGE_ID}:${ITEM_ID}`);
    expect(decoder.decode(metaAad(PACKAGE_ID, ITEM_ID))).toBe(`shareit/meta:1:${PACKAGE_ID}:${ITEM_ID}`);
    expect(metaAad(PACKAGE_ID, ITEM_ID)).not.toEqual(itemAad(PACKAGE_ID, ITEM_ID));
  });

  it('normalize both ids, so formatting cannot change the AAD', () => {
    expect(itemAad(PACKAGE_ID.toUpperCase(), ITEM_ID.replace(/-/g, ''))).toEqual(itemAad(PACKAGE_ID, ITEM_ID));
  });
});

describe('itemKey and metaKey', () => {
  it('derive separate keys for the same item', async () => {
    const key = await derivePackageKey();
    const contentKey = await itemKey(key, ITEM_ID);
    const metadataKey = await metaKey(key, ITEM_ID);

    expect(contentKey).not.toBe(metadataKey);
    expect(contentKey.algorithm.name).toBe('AES-GCM');
    expect(metadataKey.algorithm.name).toBe('AES-GCM');
  });
});
