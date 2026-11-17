import { describe, expect, it, vi } from 'vitest';
import type { ShareItem } from './use-share.ts';
import { AccessSessionExpiredError, readShareItem } from './read-item.ts';
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
    const bytes = await readShareItem(
      PACKAGE_ID,
      makeItem({ Payload: toBase64Url(utf8('hello')) }),
      undefined,
      'session-1'
    );

    expect(new TextDecoder().decode(bytes)).toBe('hello');
    expect(api.downloadContent).not.toHaveBeenCalled();
  });

  it('downloads through the access session when the payload is not inline', async () => {
    api.downloadContent.mockResolvedValue({ data: new Blob([utf8('downloaded')]) });

    const bytes = await readShareItem(PACKAGE_ID, makeItem({ Payload: null }), undefined, 'session-1');

    expect(new TextDecoder().decode(bytes)).toBe('downloaded');
    expect(api.downloadContent).toHaveBeenCalledWith(PACKAGE_ID, ITEM_ID, 'session-1');
  });

  it('opens a sealed payload with the package key', async () => {
    const key = await deriveKeyBits('correct horse battery', await packageSalt(PACKAGE_ID));
    const sealed = await seal(await itemKey(key, ITEM_ID), utf8('sealed content'), itemAad(PACKAGE_ID, ITEM_ID));

    const bytes = await readShareItem(PACKAGE_ID, makeItem({ Payload: toBase64Url(sealed) }), key, undefined);

    expect(new TextDecoder().decode(bytes)).toBe('sealed content');
  });

  it('throws the API error when the download fails', async () => {
    api.downloadContent.mockResolvedValue({ status: 404, error: new Error('session expired') });

    await expect(readShareItem(PACKAGE_ID, makeItem({ Payload: null }), undefined, 'stale-session')).rejects.toThrow(
      'session expired'
    );
  });

  it('reports a package-gone download without treating it as an expired session', async () => {
    api.downloadContent.mockResolvedValue({ status: 410, error: new Error('package expired') });
    const onSessionExpired = vi.fn();
    const onPackageGone = vi.fn();

    await expect(
      readShareItem(PACKAGE_ID, makeItem({ Payload: null }), undefined, 'session-1', onSessionExpired, onPackageGone)
    ).rejects.toThrow('package expired');

    expect(onSessionExpired).not.toHaveBeenCalled();
    expect(onPackageGone).toHaveBeenCalledTimes(1);
  });

  it('throws when the download succeeds without a body', async () => {
    api.downloadContent.mockResolvedValue({ status: 200 });

    await expect(readShareItem(PACKAGE_ID, makeItem({ Payload: null }), undefined, 'session-1')).rejects.toThrow(
      'Download failed'
    );
  });

  it('reports an expired session on a 401 download', async () => {
    api.downloadContent.mockResolvedValue({ status: 401, error: new Error('session expired') });
    const onSessionExpired = vi.fn();

    await expect(
      readShareItem(PACKAGE_ID, makeItem({ Payload: null }), undefined, 'stale-session', onSessionExpired)
    ).rejects.toThrow('session expired');

    expect(onSessionExpired).toHaveBeenCalledWith('stale-session');
  });

  it('throws when an item has no payload and the session is missing', async () => {
    const onSessionExpired = vi.fn();

    await expect(readShareItem(PACKAGE_ID, makeItem(), undefined, undefined, onSessionExpired)).rejects.toBeInstanceOf(
      AccessSessionExpiredError
    );

    expect(onSessionExpired).toHaveBeenCalledWith(undefined);
  });
});
