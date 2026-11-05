import { describe, expect, it, vi } from 'vitest';
import type { ShareItem } from './use-share.ts';
import { readShareItem } from './read-item.ts';
import { toBase64Url } from '../../shared/crypto/base64url.ts';
import { utf8 } from '../../shared/crypto/bytes.ts';
import { deriveKeyBits, itemAad, itemKey, packageSalt } from '../../shared/crypto/keys.ts';
import { seal } from '../../shared/crypto/sealed.ts';

const api = vi.hoisted(() => ({
  downloadContent: vi.fn()
}));

vi.mock('../../shared/api/content.ts', () => api);

const PACKAGE_ID = '0192f3ab-7cde-7def-8abc-0123456789ab';
const ITEM_ID = '0192f3ab-7cde-7def-8abc-0123456789ac';

const makeItem = (overrides: Partial<ShareItem> = {}): ShareItem => ({
  Id: ITEM_ID,
  Kind: 'text',
  Size: 5,
  index: 0,
  decodedMeta: null,
  displayName: 'content.txt',
  mime: 'text/plain',
  ...overrides
});

describe('readShareItem', () => {
  it('reads an inline payload without downloading', async () => {
    const bytes = await readShareItem(PACKAGE_ID, makeItem({ Payload: toBase64Url(utf8('hello')) }), undefined);

    expect(new TextDecoder().decode(bytes)).toBe('hello');
    expect(api.downloadContent).not.toHaveBeenCalled();
  });

  it('downloads through the single-use token when the payload is not inline', async () => {
    api.downloadContent.mockResolvedValue({ data: new Blob([utf8('downloaded')]) });

    const bytes = await readShareItem(PACKAGE_ID, makeItem({ Payload: null, DownloadToken: 'token-1' }), undefined);

    expect(new TextDecoder().decode(bytes)).toBe('downloaded');
    expect(api.downloadContent).toHaveBeenCalledWith(PACKAGE_ID, ITEM_ID, 'token-1');
  });

  it('opens a sealed payload with the package key', async () => {
    const key = await deriveKeyBits('correct horse battery', await packageSalt(PACKAGE_ID));
    const sealed = await seal(await itemKey(key, ITEM_ID), utf8('sealed content'), itemAad(PACKAGE_ID, ITEM_ID));

    const bytes = await readShareItem(PACKAGE_ID, makeItem({ Payload: toBase64Url(sealed) }), key);

    expect(new TextDecoder().decode(bytes)).toBe('sealed content');
  });

  it('throws the API error when the token download fails', async () => {
    api.downloadContent.mockResolvedValue({ status: 404, error: new Error('token expired') });

    await expect(
      readShareItem(PACKAGE_ID, makeItem({ Payload: null, DownloadToken: 'stale-token' }), undefined)
    ).rejects.toThrow('token expired');
  });

  it('throws when the download succeeds without a body', async () => {
    api.downloadContent.mockResolvedValue({ status: 200 });

    await expect(
      readShareItem(PACKAGE_ID, makeItem({ Payload: null, DownloadToken: 'token-1' }), undefined)
    ).rejects.toThrow('Download failed');
  });

  it('throws when an item has neither a payload nor a token', async () => {
    await expect(readShareItem(PACKAGE_ID, makeItem(), undefined)).rejects.toThrow('neither an inline payload');
  });
});
