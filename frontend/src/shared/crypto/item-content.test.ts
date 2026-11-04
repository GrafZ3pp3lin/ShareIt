import { describe, expect, it, vi } from 'vitest';

import { toBase64Url } from './base64url.ts';
import { utf8 } from './bytes.ts';
import { openItemContent, readItemBytes } from './item-content.ts';
import { deriveKeyBits, itemAad, itemKey, packageSalt } from './keys.ts';
import { seal } from './sealed.ts';

const PACKAGE_ID = '0192f3a0-1111-7000-8000-000000000000';
const ITEM_ID = '0192f3a0-3333-7000-8000-000000000000';

const derivePackageKey = async () => deriveKeyBits('correct horse battery staple', await packageSalt(PACKAGE_ID));

describe('readItemBytes', () => {
  it('decodes an inline payload without downloading', async () => {
    const download = vi.fn();
    const bytes = await readItemBytes({ Id: ITEM_ID, Payload: toBase64Url(utf8('inline')) }, download);

    expect(new TextDecoder().decode(bytes)).toBe('inline');
    expect(download).not.toHaveBeenCalled();
  });

  it('downloads through the token when no payload is present', async () => {
    const download = vi.fn(async () => new Blob([utf8('downloaded')]));

    const bytes = await readItemBytes({ Id: ITEM_ID, DownloadToken: 'token-1' }, download);

    expect(new TextDecoder().decode(bytes)).toBe('downloaded');
    expect(download).toHaveBeenCalledWith('token-1');
  });

  it('throws when an item has neither payload nor token', async () => {
    await expect(readItemBytes({ Id: ITEM_ID }, vi.fn())).rejects.toThrow('neither an inline payload');
  });
});

describe('openItemContent', () => {
  it('opens a sealed item with the package key and item AAD', async () => {
    const key = await derivePackageKey();
    const sealed = await seal(await itemKey(key, ITEM_ID), utf8('protected content'), itemAad(PACKAGE_ID, ITEM_ID));

    const bytes = await openItemContent({ Id: ITEM_ID, Payload: toBase64Url(sealed) }, key, PACKAGE_ID, vi.fn());

    expect(new TextDecoder().decode(bytes)).toBe('protected content');
  });

  it('leaves unsealed bytes alone even when a key is present', async () => {
    const key = await derivePackageKey();

    const bytes = await openItemContent({ Id: ITEM_ID, Payload: toBase64Url(utf8('plain')) }, key, PACKAGE_ID, vi.fn());

    expect(new TextDecoder().decode(bytes)).toBe('plain');
  });

  it('fails when the blob belongs to another item', async () => {
    const key = await derivePackageKey();
    const sealed = await seal(await itemKey(key, ITEM_ID), utf8('protected content'), itemAad(PACKAGE_ID, ITEM_ID));

    await expect(
      openItemContent(
        { Id: '0192f3a0-4444-7000-8000-000000000000', Payload: toBase64Url(sealed) },
        key,
        PACKAGE_ID,
        vi.fn()
      )
    ).rejects.toThrow();
  });
});
