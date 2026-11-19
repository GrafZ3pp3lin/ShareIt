import { describe, expect, it } from 'vitest';

import { utf8 } from './bytes.ts';
import { deriveKeyBits, frameAad, itemKey, metaKey, packageSalt } from './keys.ts';
import { buildMeta, decodeMeta, encodeMeta, parseMeta } from './meta.ts';
import { seal } from './framed.ts';

const PACKAGE_ID = '0192f3a0-1111-7000-8000-000000000000';
const ITEM_ID = '0192f3a0-3333-7000-8000-000000000000';

const derivePackageKey = async () => deriveKeyBits('correct horse battery staple', await packageSalt(PACKAGE_ID));

describe('meta', () => {
  it('builds and parses the documented {name, mime} shape', () => {
    const meta = { name: 'quarterly-report.pdf', mime: 'application/pdf' };

    expect(parseMeta(buildMeta(meta))).toEqual(meta);
    expect(parseMeta(buildMeta({ name: null, mime: 'text/plain' }))).toEqual({
      name: null,
      mime: 'text/plain'
    });
  });

  it('returns null for invalid JSON or a missing field', () => {
    expect(parseMeta(utf8('not json'))).toBeNull();
    expect(parseMeta(utf8(JSON.stringify({ mime: 'text/plain' })))).toBeNull();
    expect(parseMeta(utf8(JSON.stringify({ name: 'x' })))).toBeNull();
    expect(parseMeta(utf8(JSON.stringify({ name: 1, mime: 'text/plain' })))).toBeNull();
  });

  it('keeps unprotected metadata as plaintext JSON', async () => {
    const meta = { name: 'notes.md', mime: 'text/markdown' };

    const encoded = await encodeMeta(meta, undefined, PACKAGE_ID, ITEM_ID);

    expect(parseMeta(encoded)).toEqual(meta);
    expect(await decodeMeta(encoded, undefined, PACKAGE_ID, ITEM_ID)).toEqual(meta);
  });

  it('seals protected metadata and opens it with the package key', async () => {
    const key = await derivePackageKey();
    const meta = { name: 'secret.pdf', mime: 'application/pdf' };

    const encoded = await encodeMeta(meta, key, PACKAGE_ID, ITEM_ID);

    expect(parseMeta(encoded)).toBeNull();
    expect(await decodeMeta(encoded, key, PACKAGE_ID, ITEM_ID)).toEqual(meta);
  });

  it('produces identical metadata on every sealing (deterministic)', async () => {
    const key = await derivePackageKey();
    const meta = { name: 'secret.pdf', mime: 'application/pdf' };

    const first = await encodeMeta(meta, key, PACKAGE_ID, ITEM_ID);
    const second = await encodeMeta(meta, key, PACKAGE_ID, ITEM_ID);

    expect(second).toEqual(first);
  });

  it('rejects plaintext metadata when a package key is present', async () => {
    const key = await derivePackageKey();

    await expect(decodeMeta(buildMeta({ name: 'x', mime: 'text/plain' }), key, PACKAGE_ID, ITEM_ID)).rejects.toThrow(
      'Metadata is not framed'
    );
  });

  it('rejects sealed metadata without a key', async () => {
    const key = await derivePackageKey();
    const encoded = await encodeMeta({ name: 'x', mime: 'text/plain' }, key, PACKAGE_ID, ITEM_ID);

    await expect(decodeMeta(encoded, undefined, PACKAGE_ID, ITEM_ID)).rejects.toThrow('A package key is required');
  });

  it('rejects metadata sealed for the content role', async () => {
    const key = await derivePackageKey();
    const sealedMeta = await seal(
      await itemKey(key, ITEM_ID),
      buildMeta({ name: 'x', mime: 'text/plain' }),
      frameAad(PACKAGE_ID, ITEM_ID, 'item', 0, true)
    );

    await expect(decodeMeta(sealedMeta, key, PACKAGE_ID, ITEM_ID)).rejects.toThrow();
  });

  it('rejects metadata sealed for another package', async () => {
    const key = await derivePackageKey();
    const sealedMeta = await seal(
      await metaKey(key, ITEM_ID),
      buildMeta({ name: 'x', mime: 'text/plain' }),
      frameAad(PACKAGE_ID, ITEM_ID, 'meta', 0, true)
    );

    await expect(decodeMeta(sealedMeta, key, '0192f3a0-2222-7000-8000-000000000000', ITEM_ID)).rejects.toThrow();
  });
});
