import { render } from '@solidjs/testing-library';
import { flush } from 'solid-js';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { I18nProvider } from '../../i18n/i18n.tsx';
import { fromBase64Url } from '../../shared/crypto/base64url.ts';
import { isSealed } from '../../shared/crypto/sealed.ts';
import { createComposer } from './use-composer.ts';

const api = vi.hoisted(() => ({
  createPackage: vi.fn(),
  uploadItem: vi.fn(),
  getUploadLimits: vi.fn()
}));

vi.mock('../../shared/api/content.ts', () => api);

const authState = vi.hoisted(() => ({ authenticated: false }));

vi.mock('../auth/auth-context.tsx', () => ({
  useAuth: () => ({
    isEnabled: true,
    isAuthenticated: () => authState.authenticated
  })
}));

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;

type Composer = ReturnType<typeof createComposer>;

const renderComposer = () => {
  let composer: Composer | undefined;
  const Harness = () => {
    composer = createComposer();
    return null;
  };
  render(() => (
    <I18nProvider>
      <Harness />
    </I18nProvider>
  ));
  if (!composer) {
    throw new Error('composer was not created');
  }
  return composer;
};

const makeFile = (name = 'photo.jpg') => new File(['file-bytes'], name, { type: 'image/jpeg' });

const addedItem = (callIndex: number) => api.uploadItem.mock.calls[callIndex][1];

const decodedMeta = (meta: string) => JSON.parse(new TextDecoder().decode(fromBase64Url(meta)));

describe('createComposer', () => {
  beforeEach(() => {
    authState.authenticated = false;
    api.getUploadLimits.mockResolvedValue({ data: undefined });
    api.createPackage.mockImplementation(async (request: { Id: string }) => ({ data: { Id: request.Id } }));
    api.uploadItem.mockResolvedValue({ data: { Id: 'package-1' } });
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  it('creates the package with a client id and uploads text and files without encryption', async () => {
    const composer = renderComposer();
    composer.setText('hello world');
    composer.addFiles([makeFile()]);
    flush();

    composer.handleSubmit();
    await vi.waitFor(() => expect(composer.stage()).toBe('result'));

    expect(api.createPackage).toHaveBeenCalledTimes(1);
    const request = api.createPackage.mock.calls[0][0];
    expect(request.Id).toMatch(UUID);
    expect(request.UnlockVerifier).toBeNull();
    expect(request.TimeToLiveHours).toBe(6);
    expect(request.MaxDownloadCount).toBe(1);

    expect(api.uploadItem).toHaveBeenCalledTimes(2);
    expect(addedItem(0).Kind).toBe('text');
    expect(addedItem(1).Kind).toBe('file');
    expect(decodedMeta(addedItem(0).Meta)).toEqual({ name: null, mime: 'text/plain' });
    expect(decodedMeta(addedItem(1).Meta)).toEqual({ name: 'photo.jpg', mime: 'image/jpeg' });
    expect(isSealed(fromBase64Url(addedItem(0).Meta))).toBe(false);

    expect(request.UnlockVerifier).toBeNull();
    expect(composer.result()?.link).toBe(`${window.location.origin}/s/${request.Id}`);
    expect(composer.result()?.passphrase).toBeUndefined();
    expect(composer.result()?.linkWithPassphrase).toBeUndefined();
  });

  it('derives the verifier and seals content and metadata when generating a passphrase', async () => {
    const composer = renderComposer();
    composer.setText('protected text');
    composer.setPasswordMode('generate');
    flush();
    const passphrase = composer.generatedPassphrase();

    composer.handleSubmit();
    await vi.waitFor(() => expect(composer.stage()).toBe('result'));

    const request = api.createPackage.mock.calls[0][0];
    expect(fromBase64Url(request.UnlockVerifier)).toHaveLength(32);

    const item = addedItem(0);
    expect(isSealed(fromBase64Url(item.Meta))).toBe(true);

    const fileBytes = new Uint8Array(await item.File.arrayBuffer());
    expect(isSealed(fileBytes)).toBe(true);

    expect(composer.result()?.passphrase).toBe(passphrase);
    expect(composer.result()?.linkWithPassphrase).toBe(
      `${window.location.origin}/s/${request.Id}#s=${encodeURIComponent(passphrase)}`
    );
  });

  it('keeps the package and item ids stable when retrying a failed upload', async () => {
    api.uploadItem.mockResolvedValueOnce({ error: { title: 'storage unreachable' } });

    const composer = renderComposer();
    composer.setText('hello');
    composer.addFiles([makeFile()]);
    flush();

    composer.handleSubmit();
    await vi.waitFor(() => expect(composer.error()).toContain('storage unreachable'));
    expect(composer.partial()).not.toBeNull();
    expect(composer.stage()).toBe('form');

    const firstRequest = api.createPackage.mock.calls[0][0];
    const firstItemId = addedItem(0).Id;

    composer.handleSubmit();
    await vi.waitFor(() => expect(composer.stage()).toBe('result'));

    // No second package: the retry resumes the existing one with the same ids.
    expect(api.createPackage).toHaveBeenCalledTimes(1);
    const retriedItem = api.uploadItem.mock.calls[api.uploadItem.mock.calls.length - 2];
    expect(retriedItem[0]).toBe(firstRequest.Id);
    expect(retriedItem[1].Id).toBe(firstItemId);
  });

  it('blocks submit for an invalid custom passphrase', () => {
    const composer = renderComposer();
    composer.setText('hello');
    composer.setPasswordMode('custom');
    composer.setCustomPassphrase('too short');
    flush();

    expect(composer.passphraseError()).toBe('Use at least 12 characters.');
    expect(composer.canSubmit()).toBe(false);

    composer.setCustomPassphrase('long enough passphrase');
    flush();
    expect(composer.canSubmit()).toBe(true);
  });

  it('keeps auth-only options available to signed-in users', () => {
    authState.authenticated = true;
    const composer = renderComposer();

    expect(composer.isAuthenticated()).toBe(true);
    expect(composer.downloadsLabel()).toBe('once');
    expect(composer.expiryLabel()).toBe('7 days');
  });
});
