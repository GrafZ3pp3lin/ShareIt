import { createSignal, flush } from 'solid-js';
import { render } from '@solidjs/testing-library';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ContentPackageDetailsResponse } from '../../client/model/index.ts';
import { I18nProvider } from '../../i18n/i18n.tsx';
import { toBase64Url } from '../../shared/crypto/base64url.ts';
import { utf8 } from '../../shared/crypto/bytes.ts';
import { deriveKeyBits, packageSalt } from '../../shared/crypto/keys.ts';
import { encodeMeta } from '../../shared/crypto/meta.ts';
import { createShare } from './use-share.ts';

const api = vi.hoisted(() => ({
  getPackageDetails: vi.fn()
}));

vi.mock('../../shared/api/content.ts', () => api);

const PACKAGE_ID = '0192f3ab-7cde-7def-8abc-0123456789ab';
const OTHER_PACKAGE_ID = '0192f3ab-7cde-7def-8abc-0123456789ac';
const TEXT_ID = '0192f3ab-7cde-7def-8abc-0123456789ad';
const FILE_ID = '0192f3ab-7cde-7def-8abc-0123456789ae';

const packageDetails = (): ContentPackageDetailsResponse => ({
  Package: {
    Id: PACKAGE_ID,
    CreatedAt: new Date().toISOString(),
    ItemCount: 1,
    MaxDownloadCount: 1,
    CurrentDownloadCount: 0,
    RemainingDownloads: 1,
    ExpiresAt: new Date(Date.now() + 60 * 60 * 1000).toISOString()
  },
  Items: [
    {
      Id: TEXT_ID,
      Kind: 'text',
      Size: 5,
      Meta: toBase64Url(utf8(JSON.stringify({ name: null, mime: 'text/plain' }))),
      Payload: toBase64Url(utf8('hello'))
    }
  ]
});

type Share = ReturnType<typeof createShare>;

let share: Share | undefined;
let setPackageId: (id: string) => string;

const renderShare = (initialId = PACKAGE_ID) => {
  const [packageId, setId] = createSignal(initialId);
  setPackageId = setId;

  const Harness = () => {
    share = createShare(packageId);
    return null;
  };

  render(() => (
    <I18nProvider>
      <Harness />
    </I18nProvider>
  ));
  flush();

  if (!share) {
    throw new Error('share state was not created');
  }
  return share;
};

describe('createShare', () => {
  beforeEach(() => {
    window.history.replaceState(null, '', `/s/${PACKAGE_ID}`);
    api.getPackageDetails.mockReset();
    api.getPackageDetails.mockResolvedValue({ status: 200, data: packageDetails() });
  });

  it('starts locked and makes no request without an unlock', () => {
    const state = renderShare();

    expect(state.status()).toBe('locked');
    expect(state.passphrase()).toBe('');
    expect(api.getPackageDetails).not.toHaveBeenCalled();
  });

  it('goes to gone without a package id', async () => {
    const state = renderShare('');

    state.unlock();

    await vi.waitFor(() => expect(state.status()).toBe('gone'));
    expect(api.getPackageDetails).not.toHaveBeenCalled();
  });

  it('sends no proof when the gate is submitted empty', async () => {
    const state = renderShare();

    state.unlock();

    await vi.waitFor(() => expect(state.status()).toBe('unlocked'));
    expect(api.getPackageDetails).toHaveBeenCalledWith(PACKAGE_ID, undefined);
  });

  it.each([400, 404])('treats an HTTP %i as gone', async (httpStatus) => {
    api.getPackageDetails.mockResolvedValueOnce({ status: httpStatus, error: { title: 'Gone' } });
    const state = renderShare();

    state.unlock();

    await vi.waitFor(() => expect(state.status()).toBe('gone'));
    expect(state.gateMessage()).toBeNull();
  });

  it('explains a missing passphrase without claiming a wrong one', async () => {
    api.getPackageDetails.mockResolvedValueOnce({ status: 401, error: { title: 'Unlock required' } });
    const state = renderShare();

    state.unlock();

    await vi.waitFor(() => expect(state.status()).toBe('invalid'));
    expect(state.gateMessage()).toEqual({
      title: 'This link needs a passphrase.',
      body: 'Enter the passphrase the sender shared with you. Nothing was loaded and no access was used.'
    });
  });

  it('keeps the gate and reports a network failure', async () => {
    api.getPackageDetails.mockRejectedValueOnce(new Error('offline'));
    const state = renderShare();

    state.unlock();

    await vi.waitFor(() => expect(state.gateMessage()).not.toBeNull());
    expect(state.status()).toBe('locked');
    expect(state.gateMessage()).toEqual({ title: 'Unable to load this link.', body: 'offline' });
  });

  it('falls back to item names and default MIME types for unreadable metadata', async () => {
    api.getPackageDetails.mockResolvedValueOnce({
      status: 200,
      data: {
        ...packageDetails(),
        Items: [
          {
            Id: TEXT_ID,
            Kind: 'text',
            Size: '7',
            Meta: toBase64Url(utf8('not json')),
            Payload: toBase64Url(utf8('hello'))
          },
          {
            Id: FILE_ID,
            Kind: 'file',
            Size: '2',
            Meta: null,
            Payload: null,
            DownloadToken: 'token-1'
          }
        ]
      }
    });
    const state = renderShare();

    state.unlock();

    await vi.waitFor(() => expect(state.status()).toBe('unlocked'));
    const items = state.items();
    expect(items[0]).toMatchObject({
      displayName: 'item-1',
      mime: 'text/plain',
      Size: 7,
      decodedMeta: null
    });
    expect(items[1]).toMatchObject({
      displayName: 'item-2',
      mime: 'application/octet-stream',
      Size: 2,
      decodedMeta: null
    });
  });

  it('falls back when metadata is sealed but the link has no key', async () => {
    const key = await deriveKeyBits('correct horse battery', await packageSalt(PACKAGE_ID));
    const sealedMeta = await encodeMeta({ name: 'secret.txt', mime: 'text/plain' }, key, PACKAGE_ID, TEXT_ID);
    api.getPackageDetails.mockResolvedValueOnce({
      status: 200,
      data: {
        ...packageDetails(),
        Items: [
          {
            Id: TEXT_ID,
            Kind: 'text',
            Size: 5,
            Meta: toBase64Url(sealedMeta),
            Payload: toBase64Url(utf8('hello'))
          }
        ]
      }
    });
    const state = renderShare();

    state.unlock();

    await vi.waitFor(() => expect(state.status()).toBe('unlocked'));
    expect(state.items()[0].displayName).toBe('item-1');
  });

  it('ignores a late response after the package id changes', async () => {
    let resolveDetails: ((value: { status: number; data: ContentPackageDetailsResponse }) => void) | undefined;
    api.getPackageDetails.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          resolveDetails = resolve;
        })
    );
    const state = renderShare();

    state.unlock();
    await vi.waitFor(() => expect(api.getPackageDetails).toHaveBeenCalledTimes(1));

    setPackageId(OTHER_PACKAGE_ID);
    flush();

    resolveDetails?.({ status: 200, data: packageDetails() });
    await Promise.resolve();
    flush();

    expect(state.status()).toBe('locked');
    expect(state.details()).toBeNull();
  });

  it('stays locked and makes no request for an empty fragment passphrase', () => {
    window.history.replaceState(null, '', `/s/${PACKAGE_ID}#s=`);
    const state = renderShare();

    expect(state.status()).toBe('locked');
    expect(api.getPackageDetails).not.toHaveBeenCalled();
  });
});
