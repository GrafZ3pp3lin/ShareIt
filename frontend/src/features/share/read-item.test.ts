import { describe, expect, it, vi } from 'vitest';
import type { ShareItem } from './use-share.ts';
import { AccessSessionExpiredError, fetchShareItem, readShareItem } from './read-item.ts';
import { utf8 } from '../../shared/crypto/bytes.ts';
import { seal } from '../../shared/crypto/framed.ts';
import { deriveKeyBits, frameAad, itemKey, packageSalt } from '../../shared/crypto/keys.ts';

const api = vi.hoisted(() => ({
  downloadContent: vi.fn(),
  downloadContentResponse: vi.fn()
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
  it('downloads every item through the access session', async () => {
    api.downloadContent.mockResolvedValue({ data: new Blob([utf8('downloaded')]) });

    const bytes = await readShareItem(PACKAGE_ID, makeItem(), undefined, 'session-1');

    expect(new TextDecoder().decode(bytes)).toBe('downloaded');
    expect(api.downloadContent).toHaveBeenCalledWith(PACKAGE_ID, ITEM_ID, 'session-1');
  });

  it('opens a framed payload with the package key', async () => {
    const key = await deriveKeyBits('correct horse battery', await packageSalt(PACKAGE_ID));
    const framed = await seal(
      await itemKey(key, ITEM_ID),
      utf8('sealed content'),
      frameAad(PACKAGE_ID, ITEM_ID, 'item', 0, true)
    );
    api.downloadContent.mockResolvedValue({ data: new Blob([framed]) });

    const bytes = await readShareItem(PACKAGE_ID, makeItem(), key, 'session-1');

    expect(new TextDecoder().decode(bytes)).toBe('sealed content');
  });

  it('throws the API error when the download fails', async () => {
    api.downloadContent.mockResolvedValue({ status: 404, error: new Error('session expired') });

    await expect(readShareItem(PACKAGE_ID, makeItem(), undefined, 'stale-session')).rejects.toThrow('session expired');
  });

  it('does not report package gone when only the content blob is missing', async () => {
    api.downloadContent.mockResolvedValue({ status: 404, error: { title: 'Content not found' } });
    const onPackageGone = vi.fn();

    await expect(
      readShareItem(PACKAGE_ID, makeItem(), undefined, 'session-1', undefined, onPackageGone)
    ).rejects.toMatchObject({ title: 'Content not found' });

    expect(onPackageGone).not.toHaveBeenCalled();
  });

  it('reports a package-level 404 as package gone', async () => {
    api.downloadContent.mockResolvedValue({
      status: 404,
      error: { type: 'urn:shareit:problem:package-gone', title: 'Package not found' }
    });
    const onPackageGone = vi.fn();

    await expect(
      readShareItem(PACKAGE_ID, makeItem(), undefined, 'session-1', undefined, onPackageGone)
    ).rejects.toMatchObject({ title: 'Package not found' });

    expect(onPackageGone).toHaveBeenCalledTimes(1);
  });

  it('reports a package-gone download without treating it as an expired session', async () => {
    api.downloadContent.mockResolvedValue({ status: 410, error: new Error('package expired') });
    const onSessionExpired = vi.fn();
    const onPackageGone = vi.fn();

    await expect(
      readShareItem(PACKAGE_ID, makeItem(), undefined, 'session-1', onSessionExpired, onPackageGone)
    ).rejects.toThrow('package expired');

    expect(onSessionExpired).not.toHaveBeenCalled();
    expect(onPackageGone).toHaveBeenCalledTimes(1);
  });

  it('throws when the download succeeds without a body', async () => {
    api.downloadContent.mockResolvedValue({ status: 200 });

    await expect(readShareItem(PACKAGE_ID, makeItem(), undefined, 'session-1')).rejects.toThrow('Download failed');
  });

  it('reports an expired session on a 401 download', async () => {
    api.downloadContent.mockResolvedValue({ status: 401, error: new Error('session expired') });
    const onSessionExpired = vi.fn();

    await expect(readShareItem(PACKAGE_ID, makeItem(), undefined, 'stale-session', onSessionExpired)).rejects.toThrow(
      'session expired'
    );

    expect(onSessionExpired).toHaveBeenCalledWith('stale-session');
  });

  it('throws when the session is missing', async () => {
    const onSessionExpired = vi.fn();

    await expect(readShareItem(PACKAGE_ID, makeItem(), undefined, undefined, onSessionExpired)).rejects.toBeInstanceOf(
      AccessSessionExpiredError
    );

    expect(onSessionExpired).toHaveBeenCalledWith(undefined);
    expect(api.downloadContent).not.toHaveBeenCalled();
  });
});

describe('fetchShareItem', () => {
  it('returns the raw response for a streaming save', async () => {
    const response = new Response('streamed');
    api.downloadContentResponse.mockResolvedValue({ response, status: 200 });

    await expect(fetchShareItem(PACKAGE_ID, makeItem(), 'session-1')).resolves.toBe(response);
    expect(api.downloadContentResponse).toHaveBeenCalledWith(PACKAGE_ID, ITEM_ID, 'session-1');
  });

  it('reports an expired session on a 401 download', async () => {
    api.downloadContentResponse.mockResolvedValue({ status: 401, error: new Error('session expired') });
    const onSessionExpired = vi.fn();

    await expect(fetchShareItem(PACKAGE_ID, makeItem(), 'stale-session', onSessionExpired)).rejects.toThrow(
      'session expired'
    );

    expect(onSessionExpired).toHaveBeenCalledWith('stale-session');
  });

  it('does not report package gone for a content-level 404', async () => {
    api.downloadContentResponse.mockResolvedValue({ status: 404, error: { title: 'Content not found' } });
    const onPackageGone = vi.fn();

    await expect(fetchShareItem(PACKAGE_ID, makeItem(), 'session-1', undefined, onPackageGone)).rejects.toMatchObject({
      title: 'Content not found'
    });

    expect(onPackageGone).not.toHaveBeenCalled();
  });

  it('reports a package-gone download on a 410 response', async () => {
    api.downloadContentResponse.mockResolvedValue({ status: 410, error: new Error('package expired') });
    const onPackageGone = vi.fn();

    await expect(fetchShareItem(PACKAGE_ID, makeItem(), 'session-1', undefined, onPackageGone)).rejects.toThrow(
      'package expired'
    );

    expect(onPackageGone).toHaveBeenCalledTimes(1);
  });
});
