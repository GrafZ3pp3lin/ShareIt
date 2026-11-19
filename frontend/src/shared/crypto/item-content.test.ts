import { describe, expect, it } from 'vitest';

import { utf8 } from './bytes.ts';
import { openItemContent } from './item-content.ts';
import { deriveKeyBits, frameAad, itemKey, packageSalt } from './keys.ts';
import { seal } from './framed.ts';

const PACKAGE_ID = '0192f3a0-1111-7000-8000-000000000000';
const ITEM_ID = '0192f3a0-3333-7000-8000-000000000000';

const derivePackageKey = async () => deriveKeyBits('correct horse battery staple', await packageSalt(PACKAGE_ID));

describe('openItemContent', () => {
  it('opens a framed item with the package key and item AAD', async () => {
    const key = await derivePackageKey();
    const framed = await seal(
      await itemKey(key, ITEM_ID),
      utf8('protected content'),
      frameAad(PACKAGE_ID, ITEM_ID, 'item', 0, true)
    );

    const bytes = await openItemContent(PACKAGE_ID, ITEM_ID, key, framed);

    expect(new TextDecoder().decode(bytes)).toBe('protected content');
  });

  it('leaves raw bytes alone when no key is present', async () => {
    const bytes = await openItemContent(PACKAGE_ID, ITEM_ID, undefined, utf8('plain'));

    expect(new TextDecoder().decode(bytes)).toBe('plain');
  });

  it('rejects raw bytes when a package key is present', async () => {
    const key = await derivePackageKey();

    await expect(openItemContent(PACKAGE_ID, ITEM_ID, key, utf8('plain'))).rejects.toThrow('Payload is not framed');
  });

  it('fails when the blob belongs to another item', async () => {
    const key = await derivePackageKey();
    const framed = await seal(
      await itemKey(key, ITEM_ID),
      utf8('protected content'),
      frameAad(PACKAGE_ID, ITEM_ID, 'item', 0, true)
    );

    await expect(openItemContent(PACKAGE_ID, '0192f3a0-4444-7000-8000-000000000000', key, framed)).rejects.toThrow();
  });
});
